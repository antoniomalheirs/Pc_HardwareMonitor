using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Monitor_Pc.Models;
using Monitor_Pc.Utilities;

namespace Monitor_Pc.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        private readonly DispatcherTimer      _timer;
        private readonly HWiNFOReader         _reader   = new();
        private readonly CpuFallbackTelemetry _fallback;

        public ObservableCollection<HardwareItem> HardwareItems { get; } = new();

        [ObservableProperty] private string maxTemp       = "--";
        [ObservableProperty] private string cpuLoad       = "--";
        [ObservableProperty] private string ramUsage      = "--";
        [ObservableProperty] private double ramPercentage;
        [ObservableProperty] private bool   hwInfoActive;

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
                HwInfoActive = ok;
                if (ok) RebuildFromHWiNFO();
                else    RebuildFromFallback();
            }
            catch { }
        }

        // ═════════════════════════════════════════════════════════════════════
        // HWiNFO — pure passthrough, exact names, merged groups
        // ═════════════════════════════════════════════════════════════════════

        private void RebuildFromHWiNFO()
        {
            // Group readings by sensor index
            var rawGroups = _reader.AllReadings
                .GroupBy(r => r.SensorIndex)
                .Select(g => new RawGroup(g.Key, g.First().SensorName, g.ToList()))
                .ToList();

            // Merge sibling groups (e.g. "CPU [#0]: ..." + "CPU [#0]: ...: Enhanced")
            // into a single card, keyed by the stripped base name.
            var cards = new Dictionary<string, CardData>();

            foreach (var g in rawGroups)
            {
                string hwType  = DetectHwType(g.SensorName);
                string cardKey = StripSuffix(g.SensorName) + "::" + hwType;

                if (!cards.TryGetValue(cardKey, out var card))
                {
                    card = new CardData(g.SensorName, hwType);
                    cards[cardKey] = card;
                }
                else if (g.SensorName.Length < card.DisplayName.Length)
                {
                    // Prefer the shorter (base) name as the card title
                    card.DisplayName = g.SensorName;
                }

                card.Readings.AddRange(g.Readings);
            }

            // Build / update HardwareItems
            var seen = new HashSet<string>();
            float bestTemp = 0, bestLoad = 0;

            foreach (var (cardKey, card) in cards)
            {
                seen.Add(cardKey);

                var item = HardwareItems.FirstOrDefault(h =>
                    h.HardwareType == card.HwType &&
                    StripSuffix(h.Name) + "::" + h.HardwareType == cardKey);

                if (item == null)
                {
                    item = new HardwareItem
                    {
                        Name         = card.DisplayName,
                        HardwareType = card.HwType
                    };
                    HardwareItems.Add(item);
                }

                // Rebuild sensors — deduplicate by ReadingId, keep HWiNFO name as-is
                item.Sensors.Clear();
                var usedIds = new HashSet<uint>();

                foreach (var r in card.Readings.OrderBy(r => r.Label))
                {
                    if (!usedIds.Add(r.ReadingId)) continue;

                    string lhmType = ToLhmType(r.Type);
                    if (lhmType == "") continue;

                    item.Sensors.Add(new HardwareSensor
                    {
                        Name       = r.Label,          // ← exact HWiNFO name, no translation
                        SensorType = lhmType,
                        SensorId   = $"hwinfo::{r.ReadingId}",
                        Value      = (float)r.Value
                    });

                    // Collect header bar values
                    if (r.Type == SENSOR_READING_TYPE.SENSOR_TYPE_TEMP && r.Value > bestTemp
                        && IsMainCpuTemp(r.Label, card.HwType))
                        bestTemp = (float)r.Value;

                    if (r.Type == SENSOR_READING_TYPE.SENSOR_TYPE_USAGE
                        && card.HwType == "Cpu" && IsTotalCpuLoad(r.Label))
                        bestLoad = (float)r.Value;

                    if (r.Type == SENSOR_READING_TYPE.SENSOR_TYPE_USAGE
                        && card.HwType == "Memory"
                        && r.Label.Contains("Usage", StringComparison.OrdinalIgnoreCase))
                    {
                        RamUsage      = $"{r.Value:F1} %";
                        RamPercentage = r.Value;
                    }
                }

                item.RefreshGroups();
            }

            // Remove cards that no longer exist in HWiNFO
            var stale = HardwareItems
                .Where(h => !seen.Contains(StripSuffix(h.Name) + "::" + h.HardwareType))
                .ToList();
            foreach (var h in stale) HardwareItems.Remove(h);

            if (bestTemp > 0) MaxTemp = $"{bestTemp:F0}°";
            if (bestLoad > 0) CpuLoad = $"{bestLoad:F0}%";
        }

        // ═════════════════════════════════════════════════════════════════════
        // Fallback (HWiNFO not running) — PerfCounter + WMI
        // ═════════════════════════════════════════════════════════════════════

        private void RebuildFromFallback()
        {
            _fallback.Refresh();

            var cpu = GetOrCreate("CPU", "Cpu");
            cpu.Sensors.Clear();

            if (_fallback.TemperatureCelsius >= 20)
                cpu.Sensors.Add(Mk("CPU Temperature (WMI)", "Temperature", _fallback.TemperatureCelsius));

            if (_fallback.AverageClockMhz > 100)
                cpu.Sensors.Add(Mk("CPU Average Clock", "Clock", _fallback.AverageClockMhz));

            for (int i = 0; i < _fallback.PerCoreFrequencyMhz.Count; i++)
                if (_fallback.PerCoreFrequencyMhz[i] > 100)
                    cpu.Sensors.Add(Mk($"Core #{i} Clock", "Clock", _fallback.PerCoreFrequencyMhz[i]));

            if (_fallback.TotalLoadPercent > 0)
                cpu.Sensors.Add(Mk("CPU Total Load", "Load", _fallback.TotalLoadPercent));

            for (int i = 0; i < _fallback.PerCoreLoadPercent.Count; i++)
                cpu.Sensors.Add(Mk($"Core #{i} Load", "Load", _fallback.PerCoreLoadPercent[i]));

            if (_fallback.EstimatedWatts > 0)
                cpu.Sensors.Add(Mk("CPU Power (Estimated)", "Power", _fallback.EstimatedWatts));

            cpu.RefreshGroups();

            if (_fallback.TemperatureCelsius >= 20) MaxTemp = $"{_fallback.TemperatureCelsius:F0}°";
            if (_fallback.TotalLoadPercent    >  0) CpuLoad = $"{_fallback.TotalLoadPercent:F0}%";
        }

        // ═════════════════════════════════════════════════════════════════════
        // Helpers — only used internally, not for sensor naming
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Detects hardware category purely from the sensor's own name.
        /// Used only for icon selection and card layout — never modifies sensor names.
        /// </summary>
        private static string DetectHwType(string name)
        {
            if (Has(name, "Ryzen","Core i","Intel Core","Threadripper","EPYC","Athlon","Xeon","CPU ["))
                return "Cpu";
            if (Has(name, "GeForce","RTX","GTX","NVIDIA")) return "GpuNvidia";
            if (Has(name, "Radeon","RX ","AMD GPU"))        return "GpuAmd";
            if (Has(name, "Arc ","Intel GPU","Intel Graphics")) return "GpuIntel";
            if (Has(name, "GPU"))                            return "GpuNvidia";
            if (Has(name, "Memory","RAM","DIMM"))            return "Memory";
            if (Has(name, "Motherboard","System","ASUS","MSI","Gigabyte","ASRock","Biostar"))
                return "Motherboard";
            if (Has(name, "SSD","HDD","NVMe","Drive","Disk","Samsung","Seagate","Kingston","Crucial"))
                return "Storage";
            if (Has(name, "Network","Ethernet","Wi-Fi","WLAN","LAN")) return "Network";
            if (Has(name, "Battery"))  return "Battery";
            return "Other";
        }

        private static bool Has(string src, params string[] kw)
        {
            foreach (var k in kw)
                if (src.Contains(k, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// Removes HWiNFO's ": Enhanced / Extended / Debug" suffix so that
        /// the two CPU sensor groups share the same card key.
        /// </summary>
        private static string StripSuffix(string name) =>
            System.Text.RegularExpressions.Regex.Replace(
                name,
                @"\s*:\s*(Enhanced|Extended|Debug|Advanced|Extra)\s*$",
                "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

        private static string ToLhmType(SENSOR_READING_TYPE t) => t switch
        {
            SENSOR_READING_TYPE.SENSOR_TYPE_TEMP  => "Temperature",
            SENSOR_READING_TYPE.SENSOR_TYPE_VOLT  => "Voltage",
            SENSOR_READING_TYPE.SENSOR_TYPE_FAN   => "Fan",
            SENSOR_READING_TYPE.SENSOR_TYPE_POWER => "Power",
            SENSOR_READING_TYPE.SENSOR_TYPE_CLOCK => "Clock",
            SENSOR_READING_TYPE.SENSOR_TYPE_USAGE => "Load",
            SENSOR_READING_TYPE.SENSOR_TYPE_OTHER => "Factor",
            _                                     => ""
        };

        // These two are used only for the header bar (MaxTemp / CpuLoad),
        // not for sensor naming. They match on the original HWiNFO label.
        private static bool IsMainCpuTemp(string label, string hwType) =>
            hwType == "Cpu" &&
            (label.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
             label.Contains("Tdie", StringComparison.OrdinalIgnoreCase) ||
             label.Contains("Package", StringComparison.OrdinalIgnoreCase));

        private static bool IsTotalCpuLoad(string label) =>
            (label.Contains("Total", StringComparison.OrdinalIgnoreCase) ||
             label.Equals("CPU Usage", StringComparison.OrdinalIgnoreCase)) &&
            !label.Contains("Core", StringComparison.OrdinalIgnoreCase) &&
            !label.Contains("Thread", StringComparison.OrdinalIgnoreCase);

        private HardwareItem GetOrCreate(string name, string hwType)
        {
            var item = HardwareItems.FirstOrDefault(h => h.HardwareType == hwType);
            if (item != null) return item;
            item = new HardwareItem { Name = name, HardwareType = hwType };
            HardwareItems.Add(item);
            return item;
        }

        private static HardwareSensor Mk(string name, string type, float value) =>
            new() { Name = name, SensorType = type, SensorId = $"fb::{type}::{name}", Value = value };

        public void Dispose()
        {
            _timer.Stop();
            _reader.Dispose();
            _fallback.Dispose();
        }

        // ── Internal DTOs ─────────────────────────────────────────────────────

        private record RawGroup(uint SensorIndex, string SensorName, List<HWiNFO_Reading> Readings);

        private class CardData
        {
            public string              DisplayName { get; set; }
            public string              HwType      { get; }
            public List<HWiNFO_Reading> Readings   { get; } = new();
            public CardData(string name, string hwType) { DisplayName = name; HwType = hwType; }
        }
    }
}
