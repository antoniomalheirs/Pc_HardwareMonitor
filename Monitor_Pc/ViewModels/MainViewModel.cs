using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monitor_Pc.Models;
using Monitor_Pc.Utilities;

namespace Monitor_Pc.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        private readonly DispatcherTimer      _timer;
        private readonly HWiNFOReader         _reader   = new();
        private readonly CpuFallbackTelemetry _fallback;
        private readonly TelemetryLogger      _logger   = new();

        public ObservableCollection<HardwareItem> HardwareItems { get; } = new();

        [ObservableProperty] private string maxTemp           = "--";
        [ObservableProperty] private string cpuLoad           = "--";
        [ObservableProperty] private string ramUsage          = "--";
        [ObservableProperty] private double ramPercentage;
        [ObservableProperty] private bool   hwInfoActive;
        [ObservableProperty] private long   totalReadings;
        [ObservableProperty] private string lastUpdate        = "--";
        [ObservableProperty] private string diagnosticMessage = "";

        // ── Logger state ──────────────────────────────────────────────────────
        [ObservableProperty] private bool   isLogging;
        [ObservableProperty] private string logFilePath       = "";
        [ObservableProperty] private long   logEntryCount;
        [ObservableProperty] private string loggingStatusText = "Logger parado";

        public MainViewModel()
        {
            _fallback = new CpuFallbackTelemetry();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) => UpdateAll();
            UpdateAll();
            _timer.Start();
        }

        private void UpdateAll()
        {
            try
            {
                bool ok = _reader.Refresh();
                HwInfoActive       = ok;
                TotalReadings      = ok ? _reader.AllReadings.Count : 0;
                LastUpdate         = DateTime.Now.ToString("HH:mm:ss");
                DiagnosticMessage  = ok ? "" : _reader.DiagnosticMessage;

                if (ok) RebuildFromHWiNFO();
                else    RebuildFromFallback();

                // ── Feed logger ───────────────────────────────────────────
                if (_logger.IsActive)
                {
                    _logger.LogEntry(HardwareItems);
                    LogEntryCount    = _logger.EntryCount;
                    LoggingStatusText = $"Gravando · {_logger.EntryCount} entradas";
                }
            }
            catch { }
        }

        // ── HWiNFO path ───────────────────────────────────────────────────────

        private void RebuildFromHWiNFO()
        {
            var rawGroups = _reader.AllReadings
                .GroupBy(r => r.SensorIndex)
                .Select(g => new RawGroup(g.Key, g.First().SensorName, g.ToList()))
                .ToList();

            // Structure: hardwareType -> categoryName -> List of Readings
            var hwDict = new Dictionary<string, Dictionary<string, List<HWiNFO_Reading>>>();

            foreach (var g in rawGroups)
            {
                string hwType = DetectHwType(g.SensorName);
                if (hwType != "Cpu" && !hwType.StartsWith("Gpu") && hwType != "Motherboard")
                    continue;

                if (!hwDict.ContainsKey(hwType))
                    hwDict[hwType] = new Dictionary<string, List<HWiNFO_Reading>>();

                foreach (var r in g.Readings)
                {
                    string category = GetCategory(r.Type);
                    if (category == null) continue;

                    if (!hwDict[hwType].ContainsKey(category))
                        hwDict[hwType][category] = new List<HWiNFO_Reading>();

                    hwDict[hwType][category].Add(r);
                }
            }

            var seenHwItems = new HashSet<string>();
            float bestTemp = 0, bestLoad = 0;

            // Ordered explicitly
            var orderedHwTypes = hwDict.Keys
                .OrderBy(k => k == "Cpu" ? 0 : k == "Motherboard" ? 1 : k.StartsWith("Gpu") ? 2 : 3)
                .ToList();

            foreach (string hwType in orderedHwTypes)
            {
                seenHwItems.Add(hwType);

                string uiTitle = hwType == "Cpu" ? "PROCESSADOR (CPU)" 
                               : hwType == "Motherboard" ? "PLACA-MÃE (MB)" 
                               : hwType.Replace("Gpu", "GPU ");

                string iconStr = hwType == "Cpu" ? "\uE950" 
                               : hwType == "Motherboard" ? "\uE9A1" 
                               : hwType.StartsWith("Gpu") ? "\uE9A2" : "\uE7B3";

                var item = HardwareItems.FirstOrDefault(h => h.HardwareType == hwType);
                if (item == null)
                {
                    item = new HardwareItem
                    {
                        Name = uiTitle,
                        HardwareType = hwType,
                        Icon = iconStr
                    };
                    HardwareItems.Add(item);
                }

                var seenCategories = new HashSet<string>();
                var orderedCategories = hwDict[hwType].Keys.OrderBy(GetCategoryOrder).ToList();

                foreach (string categoryName in orderedCategories)
                {
                    seenCategories.Add(categoryName);
                    
                    var categoryObj = item.Categories.FirstOrDefault(c => c.Name == categoryName);
                    if (categoryObj == null)
                    {
                        categoryObj = new SensorCategory
                        {
                            Name = categoryName,
                            AccentColor = GetCategoryAccentColor(hwType, categoryName),
                            OrderIndex = GetCategoryOrder(categoryName)
                        };
                        
                        // Insert category keeping order
                        int insertIdx = 0;
                        while(insertIdx < item.Categories.Count && item.Categories[insertIdx].OrderIndex <= categoryObj.OrderIndex)
                            insertIdx++;
                        
                        item.Categories.Insert(insertIdx, categoryObj);
                    }

                    var readings = hwDict[hwType][categoryName];
                    var currentReadingIds = readings.Select(r => $"hwinfo::{r.ReadingId}").ToHashSet();

                    for (int i = categoryObj.Sensors.Count - 1; i >= 0; i--)
                    {
                        if (!currentReadingIds.Contains(categoryObj.Sensors[i].SensorId))
                            categoryObj.Sensors.RemoveAt(i);
                    }

                    var usedIds = new HashSet<uint>();

                    foreach (var r in readings.OrderBy(r => r.Label))
                    {
                        if (!usedIds.Add(r.ReadingId)) continue;

                        string sensorId = $"hwinfo::{r.ReadingId}";
                        var existingSensor = categoryObj.Sensors.FirstOrDefault(s => s.SensorId == sensorId);

                        if (existingSensor == null)
                        {
                            categoryObj.Sensors.Add(new HardwareSensor
                            {
                                Name       = r.Label,
                                SensorType = ToLhmType(r.Type),
                                Unit       = r.Unit,
                                SensorId   = sensorId,
                                Value      = (float)r.Value
                            });
                        }
                        else
                        {
                            existingSensor.Unit  = r.Unit;
                            existingSensor.Value = (float)r.Value;
                        }
                        
                        // Stats detection
                        if (r.Type == SENSOR_READING_TYPE.SENSOR_TYPE_TEMP && r.Value > bestTemp && IsMainCpuTemp(r.Label, hwType))
                            bestTemp = (float)r.Value;
                            
                        if (r.Type == SENSOR_READING_TYPE.SENSOR_TYPE_USAGE && hwType == "Cpu" && IsTotalCpuLoad(r.Label))
                            bestLoad = (float)r.Value;
                    }
                }

                // Cleanup stale categories
                var staleCategories = item.Categories.Where(c => !seenCategories.Contains(c.Name)).ToList();
                foreach (var c in staleCategories) item.Categories.Remove(c);
            }

            // Cleanup stale Hardware Components
            var staleHwItems = HardwareItems.Where(h => !seenHwItems.Contains(h.HardwareType)).ToList();
            foreach (var h in staleHwItems) HardwareItems.Remove(h);

            if (bestTemp > 0) MaxTemp = $"{bestTemp:F0}°";
            if (bestLoad > 0) CpuLoad = $"{bestLoad:F0}%";
        }

        // ── Fallback path ─────────────────────────────────────────────────────

        private void RebuildFromFallback()
        {
            _fallback.Refresh();
            
            var cpuItem = HardwareItems.FirstOrDefault(h => h.HardwareType == "Cpu");
            if (cpuItem == null)
            {
                cpuItem = new HardwareItem { Name = "PROCESSADOR (CPU)", HardwareType = "Cpu", Icon = "\uE950" };
                HardwareItems.Add(cpuItem);
            }

            var cpuCategories = new Dictionary<string, SensorCategory>();

            SensorCategory GetCat(string catName, string accent, int order)
            {
                if (!cpuCategories.TryGetValue(catName, out var cat))
                {
                    cat = cpuItem.Categories.FirstOrDefault(c => c.Name == catName);
                    if (cat == null)
                    {
                        cat = new SensorCategory { Name = catName, AccentColor = accent, OrderIndex = order };
                        
                        int insertIdx = 0;
                        while(insertIdx < cpuItem.Categories.Count && cpuItem.Categories[insertIdx].OrderIndex <= cat.OrderIndex)
                            insertIdx++;
                        
                        cpuItem.Categories.Insert(insertIdx, cat);
                    }
                    cat.Sensors.Clear();
                    cpuCategories[catName] = cat;
                }
                return cat;
            }

            if (_fallback.TemperatureCelsius >= 20)
                GetCat("Temperaturas", "#EF9A9A", 3).Sensors.Add(Mk("CPU Temperature (WMI)", "Temperature", "°C", _fallback.TemperatureCelsius));

            if (_fallback.AverageClockMhz > 100)
                GetCat("Frequências", "#CE93D8", 1).Sensors.Add(Mk("CPU Average Clock", "Clock", "MHz", _fallback.AverageClockMhz));
            for (int i = 0; i < _fallback.PerCoreFrequencyMhz.Count; i++)
                if (_fallback.PerCoreFrequencyMhz[i] > 100)
                    GetCat("Frequências", "#CE93D8", 1).Sensors.Add(Mk($"Core #{i} Clock", "Clock", "MHz", _fallback.PerCoreFrequencyMhz[i]));

            if (_fallback.TotalLoadPercent > 0)
                GetCat("Uso", "#B39DDB", 2).Sensors.Add(Mk("CPU Total Load", "Load", "%", _fallback.TotalLoadPercent));
            for (int i = 0; i < _fallback.PerCoreLoadPercent.Count; i++)
                GetCat("Uso", "#B39DDB", 2).Sensors.Add(Mk($"Core #{i} Load", "Load", "%", _fallback.PerCoreLoadPercent[i]));

            if (_fallback.EstimatedWatts > 0)
                GetCat("Potência", "#80CBC4", 5).Sensors.Add(Mk("CPU Power (Estimated)", "Power", "W", _fallback.EstimatedWatts));

            if (_fallback.TemperatureCelsius >= 20) MaxTemp = $"{_fallback.TemperatureCelsius:F0}°";
            if (_fallback.TotalLoadPercent    >  0) CpuLoad = $"{_fallback.TotalLoadPercent:F0}%";
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string GetCategory(SENSOR_READING_TYPE t) => t switch
        {
            SENSOR_READING_TYPE.SENSOR_TYPE_CLOCK   => "Frequências",
            SENSOR_READING_TYPE.SENSOR_TYPE_VOLT    => "Tensões",
            SENSOR_READING_TYPE.SENSOR_TYPE_TEMP    => "Temperaturas",
            SENSOR_READING_TYPE.SENSOR_TYPE_POWER   => "Potência",
            SENSOR_READING_TYPE.SENSOR_TYPE_USAGE   => "Uso",
            SENSOR_READING_TYPE.SENSOR_TYPE_FAN     => "Ventoinhas",
            _                                       => "Outros"
        };

        private static int GetCategoryOrder(string cat) => cat switch
        {
            "Frequências"  => 1,
            "Uso"          => 2,
            "Temperaturas" => 3,
            "Tensões"      => 4,
            "Potência"     => 5,
            _              => 99
        };

        private static string GetCategoryAccentColor(string hwType, string category)
        {
            return category switch
            {
                "Frequências"  => hwType.StartsWith("Gpu") ? "#90CAF9" : "#DF80FF", // Blue GPU / Purple CPU
                "Tensões"      => hwType.StartsWith("Gpu") ? "#FFF176" : hwType == "Motherboard" ? "#FFE082" : "#FFAB91", // Yellow GPU / Orange CPU
                "Temperaturas" => "#EF9A9A", // Red
                "Potência"     => "#80CBC4", // Teal
                "Uso"          => "#B39DDB", // Deep Purple
                "Ventoinhas"   => "#90CAF9", // Blue
                _              => "#AAFFFFFF"
            };
        }

        private static string ToLhmType(SENSOR_READING_TYPE t) => t switch
        {
            SENSOR_READING_TYPE.SENSOR_TYPE_TEMP    => "Temperature",
            SENSOR_READING_TYPE.SENSOR_TYPE_VOLT    => "Voltage",
            SENSOR_READING_TYPE.SENSOR_TYPE_FAN     => "Fan",
            SENSOR_READING_TYPE.SENSOR_TYPE_POWER   => "Power",
            SENSOR_READING_TYPE.SENSOR_TYPE_CLOCK   => "Clock",
            SENSOR_READING_TYPE.SENSOR_TYPE_USAGE   => "Load",
            _                                       => "Factor"
        };

        private static string DetectHwType(string name)
        {
            if (Has(name, "Ryzen","Core i","Intel Core","Threadripper","EPYC","Athlon","Xeon","CPU [")) return "Cpu";
            if (Has(name, "GeForce","RTX","GTX","NVIDIA"))                return "GpuNvidia";
            if (Has(name, "Radeon","RX ","AMD GPU"))                      return "GpuAmd";
            if (Has(name, "Arc ","Intel GPU","Intel Graphics"))           return "GpuIntel";
            if (Has(name, "GPU"))                                         return "GpuNvidia";
            if (Has(name, "Memory","RAM","DIMM"))                         return "Memory";
            if (Has(name, "Motherboard","System","ASUS","MSI","Gigabyte","ASRock","Biostar")) return "Motherboard";
            if (Has(name, "SSD","HDD","NVMe","Drive","Disk","Samsung","Seagate","Kingston","Crucial","WD")) return "Storage";
            if (Has(name, "Network","Ethernet","Wi-Fi","WLAN","LAN"))     return "Network";
            if (Has(name, "Battery"))                                     return "Battery";
            return "Other";
        }

        private static bool Has(string src, params string[] kw)
        {
            foreach (var k in kw)
                if (src.Contains(k, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string StripSuffix(string name) =>
            System.Text.RegularExpressions.Regex.Replace(
                name,
                @"\s*:\s*(Enhanced|Extended|Debug|Advanced|Extra)\s*$",
                "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

        private static bool IsMainCpuTemp(string label, string hwType) =>
            hwType == "Cpu" &&
            (label.Contains("Tctl",    StringComparison.OrdinalIgnoreCase) ||
             label.Contains("Tdie",    StringComparison.OrdinalIgnoreCase) ||
             label.Contains("Package", StringComparison.OrdinalIgnoreCase));

        private static bool IsTotalCpuLoad(string label) =>
            (label.Contains("Total", StringComparison.OrdinalIgnoreCase) ||
             label.Equals("CPU Usage", StringComparison.OrdinalIgnoreCase)) &&
            !label.Contains("Core",   StringComparison.OrdinalIgnoreCase) &&
            !label.Contains("Thread", StringComparison.OrdinalIgnoreCase);

        private HardwareItem GetOrCreate(string name, string hwType)
        {
            var item = HardwareItems.FirstOrDefault(h => h.HardwareType == hwType);
            if (item != null) return item;
            item = new HardwareItem { Name = name, HardwareType = hwType };
            HardwareItems.Add(item);
            return item;
        }

        private static HardwareSensor Mk(string name, string type, string unit, float value) =>
            new() { Name = name, SensorType = type, Unit = unit, SensorId = $"fb::{type}::{name}", Value = value };

        // ── Logger commands ────────────────────────────────────────────────

        [RelayCommand]
        private void ToggleLogging()
        {
            if (_logger.IsActive)
            {
                _logger.Stop();
                IsLogging         = false;
                LoggingStatusText = "Logger parado";
            }
            else
            {
                _logger.Start();
                IsLogging         = true;
                LogFilePath       = _logger.FilePath;
                LogEntryCount     = 0;
                LoggingStatusText = "Iniciando gravação...";
            }
        }

        [RelayCommand]
        private void OpenLogFolder()
        {
            string dir = Path.GetDirectoryName(_logger.FilePath)
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "MonitorPC_Logs");
            try { System.Diagnostics.Process.Start("explorer.exe", dir); }
            catch { }
        }

        [RelayCommand]
        private void DumpSensors()
        {
            try
            {
                if (!_reader.Refresh()) return;

                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "HWiNFO_SensorDump.txt");

                using var sw = new StreamWriter(path, false, System.Text.Encoding.UTF8);
                sw.WriteLine($"=== HWiNFO Sensor Dump === {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sw.WriteLine($"Total readings: {_reader.AllReadings.Count}");
                sw.WriteLine();

                var groups = _reader.AllReadings
                    .GroupBy(r => r.SensorIndex)
                    .OrderBy(g => g.Key);

                foreach (var g in groups)
                {
                    string sName = g.First().SensorName;
                    string detected = DetectHwType(sName);
                    sw.WriteLine($"╔══ Sensor #{g.Key}: \"{sName}\" → DetectHwType = {detected}");

                    foreach (var r in g.OrderBy(r => r.Type).ThenBy(r => r.Label))
                    {
                        sw.WriteLine($"║  [{r.Type,-25}] {r.Label,-45} = {r.Value,12:F3} {r.Unit}");
                    }
                    sw.WriteLine("╚══");
                    sw.WriteLine();
                }

                System.Diagnostics.Process.Start("notepad.exe", path);
            }
            catch { }
        }

        [ObservableProperty] private string exportStatusText = "";

        [RelayCommand]
        private void ExportToExcel()
        {
            try
            {
                string csvPath = _logger.FilePath;
                if (string.IsNullOrEmpty(csvPath) || !File.Exists(csvPath))
                {
                    // Try to find the most recent log file
                    string logsDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "MonitorPC_Logs");

                    if (!Directory.Exists(logsDir))
                    {
                        ExportStatusText = "Nenhum log encontrado";
                        return;
                    }

                    csvPath = Directory.GetFiles(logsDir, "MonitorLog_*.csv")
                        .OrderByDescending(f => f)
                        .FirstOrDefault() ?? "";

                    if (string.IsNullOrEmpty(csvPath))
                    {
                        ExportStatusText = "Nenhum CSV encontrado";
                        return;
                    }
                }

                // Stop logging temporarily to flush data
                bool wasLogging = _logger.IsActive;
                if (wasLogging) _logger.Stop();

                ExportStatusText = "Exportando...";
                string xlsxPath = ExcelExporter.ExportCsvToExcel(csvPath);
                ExportStatusText = "✅ Excel gerado!";

                // Restart logging if it was active
                if (wasLogging)
                {
                    _logger.Start();
                    IsLogging = true;
                    LogFilePath = _logger.FilePath;
                }

                // Open the Excel file
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = xlsxPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ExportStatusText = $"Erro: {ex.Message}";
            }
        }

        public void Dispose()
        {
            _timer.Stop();
            _logger.Dispose();
            _reader.Dispose();
            _fallback.Dispose();
        }

        private record RawGroup(uint SensorIndex, string SensorName, List<HWiNFO_Reading> Readings);

        private class CardData
        {
            public string DisplayName { get; set; }
            public string HwType { get; }
            public string Category { get; }
            public List<HWiNFO_Reading> Readings { get; } = new();
            public CardData(string d, string t, string c) { DisplayName = d; HwType = t; Category = c; }
        }
    }
}
