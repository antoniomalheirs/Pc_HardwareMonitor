using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Monitor_Pc.Models
{
    public partial class HardwareItem : ObservableObject
    {
        [ObservableProperty]
        private string name = string.Empty;

        [ObservableProperty]
        private string hardwareType = string.Empty;

        [ObservableProperty]
        private string icon = ""; // Default generic icon

        [ObservableProperty]
        private bool isCpu;

        [ObservableProperty]
        private bool isGpu;

        [ObservableProperty]
        private string cpuTotalLoad = "--";

        [ObservableProperty]
        private double cpuTotalLoadPercentage;

        [ObservableProperty]
        private string cpuPackageTemp = "--";

        [ObservableProperty]
        private double cpuPackageTempPercentage;

        [ObservableProperty]
        private string cpuAverageClock = "--";

        [ObservableProperty]
        private double cpuAverageClockPercentage;

        [ObservableProperty]
        private string cpuPackagePower = "--";

        [ObservableProperty]
        private string cpuCoreVoltage = "--";

        [ObservableProperty]
        private string cpuPpt = "--";

        [ObservableProperty]
        private string cpuTdc = "--";

        [ObservableProperty]
        private string cpuEdc = "--";

        [ObservableProperty]
        private string cpuPl1 = "--";

        [ObservableProperty]
        private string cpuPl2 = "--";

        [ObservableProperty]
        private string cpuCoreMaxTemp = "--";

        [ObservableProperty]
        private string cpuSocVoltage = "--";

        [ObservableProperty]
        private string cpuVidVoltage = "--";

        [ObservableProperty]
        private string gpuCoreVoltage = "--";

        [ObservableProperty]
        private string gpuMemVoltage = "--";

        [ObservableProperty]
        private string gpuSocVoltage = "--";

        [ObservableProperty]
        private string gpuLoad = "--";

        [ObservableProperty]
        private string gpuCoreTemp = "--";

        [ObservableProperty]
        private string gpuHotSpotTemp = "--";

        [ObservableProperty]
        private string gpuCoreClock = "--";

        [ObservableProperty]
        private string gpuMemClock = "--";

        [ObservableProperty]
        private double cpuCoreMaxTempPercentage;

        partial void OnHardwareTypeChanged(string value)
        {
            IsCpu = value == "Cpu";
            IsGpu = value.StartsWith("Gpu");

            Icon = value switch
            {
                "Cpu" => "",
                "GpuNvidia" or "GpuAmd" or "GpuIntel" => "",
                "Memory" => "",
                "Motherboard" => "",
                "Storage" => "",
                _ => ""
            };
        }

        public ObservableCollection<HardwareSensor> Sensors { get; } = new ObservableCollection<HardwareSensor>();

        [ObservableProperty]
        private ObservableCollection<HardwareSensor> temperatures = new();

        [ObservableProperty]
        private ObservableCollection<HardwareSensor> loads = new();

        [ObservableProperty]
        private ObservableCollection<HardwareSensor> clocks = new();

        [ObservableProperty]
        private ObservableCollection<HardwareSensor> voltages = new();

        [ObservableProperty]
        private ObservableCollection<HardwareSensor> power = new();

        [ObservableProperty]
        private ObservableCollection<HardwareSensor> fans = new();

        [ObservableProperty]
        private bool hasTemperatures;
        [ObservableProperty]
        private bool hasLoads;
        [ObservableProperty]
        private bool hasClocks;
        [ObservableProperty]
        private bool hasVoltages;
        [ObservableProperty]
        private bool hasPower;
        [ObservableProperty]
        private bool hasFans;

        [ObservableProperty]
        private ObservableCollection<HardwareSensor> current = new();
        [ObservableProperty]
        private ObservableCollection<HardwareSensor> throughput = new();
        [ObservableProperty]
        private ObservableCollection<HardwareSensor> level = new();
        [ObservableProperty]
        private ObservableCollection<HardwareSensor> factor = new();

        [ObservableProperty]
        private bool hasCurrent;
        [ObservableProperty]
        private bool hasThroughput;
        [ObservableProperty]
        private bool hasLevel;
        [ObservableProperty]
        private bool hasFactor;

        public void RefreshGroups()
        {
            UpdateGroup(Temperatures, "Temperature");
            UpdateGroup(Loads, "Load");
            UpdateGroup(Clocks, "Clock");
            UpdateGroup(Voltages, "Voltage");
            UpdateGroup(Power, "Power");
            UpdateGroup(Fans, "Fan");
            UpdateGroup(Current, "Current");
            UpdateGroup(Throughput, "Throughput");
            UpdateGroup(Throughput, "Data"); // Merge Data into Throughput
            UpdateGroup(Level, "Level");
            UpdateGroup(Factor, "Factor");

            HasTemperatures = Temperatures.Count > 0;
            HasLoads = Loads.Count > 0;
            HasClocks = Clocks.Count > 0;
            HasVoltages = Voltages.Count > 0;
            HasPower = Power.Count > 0;
            HasFans = Fans.Count > 0;
            HasCurrent = Current.Count > 0;
            HasThroughput = Throughput.Count > 0;
            HasLevel = Level.Count > 0;
            HasFactor = Factor.Count > 0;

            if (IsCpu)
            {
                RefreshCpuOverview();
            }
            if (IsGpu)
            {
                RefreshGpuOverview();
            }
        }

        private void RefreshGpuOverview()
        {
            var coreVoltage = PickPreferredSensor(Voltages, "Core", "GPU Voltage", "VDDC", "Chip", "Vcore");
            GpuCoreVoltage = coreVoltage?.FormattedValue ?? "--";

            var memVoltage = PickPreferredSensor(Voltages, "Mem", "MVDDC", "Memory Voltage", "VRAM");
            GpuMemVoltage = memVoltage?.FormattedValue ?? "--";

            var socVoltage = PickPreferredSensor(Voltages, "SOC", "GPU SOC", "VDDCI");
            GpuSocVoltage = socVoltage?.FormattedValue ?? "--";

            var gpuLoadSensor = PickPreferredSensor(Loads, "Total", "GPU Core", "Usage", "Load");
            GpuLoad = gpuLoadSensor?.FormattedValue ?? "--";

            var coreTemp = PickPreferredSensor(Temperatures, "Core", "GPU Core", "Package", "Chip");
            GpuCoreTemp = coreTemp?.FormattedValue ?? "--";

            var hotSpot = PickPreferredSensor(Temperatures, "Hot Spot", "Junction", "Max");
            GpuHotSpotTemp = hotSpot?.FormattedValue ?? "--";

            var coreClock = PickPreferredSensor(Clocks, "Core", "GPU Clock", "Graphics");
            GpuCoreClock = coreClock?.FormattedValue ?? "--";

            var memClock = PickPreferredSensor(Clocks, "Mem", "Memory", "VRAM");
            GpuMemClock = memClock?.FormattedValue ?? "--";
        }

        private void RefreshCpuOverview()
        {
            var preferredLoad = PickPreferredSensor(Loads,
                "Total", "Usage", "Load", "Package", "Core Max", "Carga", "Uso");
            CpuTotalLoad = preferredLoad?.FormattedValue ?? "--";
            CpuTotalLoadPercentage = preferredLoad?.ValuePercentage ?? 0;
 
            var preferredTemp = PickPreferredSensor(Temperatures,
                "Package", "Tctl", "Tdie", "Average", "CCD", "Core (Tctl/Tdie)", "Die", "Temperatura", "Temp");
            CpuPackageTemp = preferredTemp?.FormattedValue ?? "--";
            CpuPackageTempPercentage = preferredTemp?.ValuePercentage ?? 0;

            var averageClock = Clocks
                .Where(s => s.Name.Contains("Core", System.StringComparison.OrdinalIgnoreCase) ||
                            s.Name.Contains("Effective", System.StringComparison.OrdinalIgnoreCase) ||
                            s.Name.Contains("Average", System.StringComparison.OrdinalIgnoreCase))
                .Where(s => s.IsValid && s.Value > 0)
                .Select(s => s.Value!.Value)
                .DefaultIfEmpty()
                .Average();

            if (averageClock > 0)
            {
                CpuAverageClock = averageClock >= 1000
                    ? $"{averageClock / 1000f:F2} GHz"
                    : $"{averageClock:F0} MHz";
                CpuAverageClockPercentage = System.Math.Clamp(averageClock / 5500f * 100, 0, 100);
            }
            else
            {
                CpuAverageClock = "--";
                CpuAverageClockPercentage = 0;
            }

            var preferredPower = PickPreferredSensor(Power,
                "Package", "SMU", "Core", "PPT", "CPU", "Consumo");
            CpuPackagePower = preferredPower?.FormattedValue ?? "--";

            var preferredVoltage = PickPreferredSensor(Voltages,
                "SVI2 TFN", "Core (SVI2 TFN)", "SVI2", "Vcore", "CPU Voltage", "Core Voltage", "Tensão");
            // If we found a 1.55V VID and have an SVI2, prefer SVI2
            CpuCoreVoltage = preferredVoltage?.FormattedValue ?? "--";

            var socVoltage = PickPreferredSensor(Voltages, "SVI2 TFN", "SOC (SVI2 TFN)", "SOC", "Uncore", "NB", "SOC Voltage");
            CpuSocVoltage = socVoltage?.FormattedValue ?? "--";

            var vidVoltage = PickPreferredSensor(Voltages, "VID", "Core VID");
            CpuVidVoltage = vidVoltage?.FormattedValue ?? "--";

            var ppt = PickPreferredSensor(Power, "PPT", "Package Power", "Consumo PPT");
            CpuPpt = ppt?.FormattedValue ?? "--";

            var tdc = PickPreferredSensor(Sensors.Where(s => s.SensorType == "Current"), "TDC", "Core Current", "Amperagem");
            CpuTdc = tdc?.FormattedValue ?? "--";

            var edc = PickPreferredSensor(Sensors.Where(s => s.SensorType == "Current"), "EDC", "Core Current", "Amperagem");
            CpuEdc = edc?.FormattedValue ?? "--";

            var pl1 = PickPreferredSensor(Power, "PL1", "Power Limit 1", "Limite");
            CpuPl1 = pl1?.FormattedValue ?? "--";

            var pl2 = PickPreferredSensor(Power, "PL2", "Power Limit 2", "Limite");
            CpuPl2 = pl2?.FormattedValue ?? "--";

            var coreMax = PickPreferredSensor(Temperatures, "Core Max", "Core Temperature", "Máxima");
            CpuCoreMaxTemp = coreMax?.FormattedValue ?? "--";
            CpuCoreMaxTempPercentage = coreMax?.ValuePercentage ?? 0;

            PairLoadWithClock();
        }

        private void PairLoadWithClock()
        {
            foreach (var load in Loads)
            {
                if (load.LinkedSensor != null) continue;

                var match = System.Text.RegularExpressions.Regex.Match(load.Name, @"(Core|Núcleo)\s*#?\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    int coreIdx = int.Parse(match.Groups[2].Value);
                    var clock = Clocks.FirstOrDefault(c => 
                        (c.Name.Contains($"#{coreIdx}") || c.Name.Contains($"#{coreIdx - 1}") || c.Name.EndsWith($" {coreIdx}") || c.Name.EndsWith($" {coreIdx - 1}")) && 
                        (c.Name.Contains("Core", System.StringComparison.OrdinalIgnoreCase) || c.Name.Contains("Relógio", System.StringComparison.OrdinalIgnoreCase)) &&
                        (c.Name.Contains("Clock", System.StringComparison.OrdinalIgnoreCase) || c.Name.Contains("Frequência", System.StringComparison.OrdinalIgnoreCase)));

                    if (clock != null)
                    {
                        load.LinkedSensor = clock;
                    }
                }
            }
        }

        private static HardwareSensor? PickPreferredSensor(IEnumerable<HardwareSensor> source, params string[] keywords)
        {
            var validSensors = source.Where(s => s.IsValid).ToList();
            if (!validSensors.Any()) return null;

            // ABSOLUTE PRIORITY: CPU Voltage (SVI2 TFN)
            if (keywords.Any(k => k.Equals("SVI2 TFN", StringComparison.OrdinalIgnoreCase)))
            {
                // Prefer SVI2 TFN that is NOT static 1.55V
                var tfn = validSensors.FirstOrDefault(s => s.Name.Contains("TFN", StringComparison.OrdinalIgnoreCase) && Math.Abs((s.Value ?? 0) - 1.55f) > 0.01f);
                if (tfn != null) return tfn;
                
                // Fallback to any TFN
                tfn = validSensors.FirstOrDefault(s => s.Name.Contains("TFN", StringComparison.OrdinalIgnoreCase));
                if (tfn != null) return tfn;
            }

            return validSensors
                .OrderBy(s => {
                    // Prefer sensors with specified IDs (fallbacks often have explicit IDs)
                    if (!string.IsNullOrEmpty(s.SensorId) && keywords.Any(k => s.SensorId.Contains(k, StringComparison.OrdinalIgnoreCase)))
                        return 0;

                    for (int i = 0; i < keywords.Length; i++)
                    {
                        if (s.Name.Contains(keywords[i], StringComparison.OrdinalIgnoreCase))
                            return i + 1;
                    }
                    return 999;
                })
                .ThenBy(s => {
                    // De-prioritize static 1.55V if we are looking for voltages
                    if (s.SensorType == "Voltage" && Math.Abs((s.Value ?? 0) - 1.55f) < 0.001f)
                        return 1;
                    return 0;
                })
                .ThenByDescending(s => s.Value)
                .FirstOrDefault();
        }

        private void UpdateGroup(ObservableCollection<HardwareSensor> group, string type)
        {
            var query = Sensors.Where(s => s.SensorType == type && s.IsValid);
            bool isCpuCard = HardwareType == "Cpu";

            // We only keep basic sorting, no more "Useful" filtering that might hide sensors
            var targetSensors = query
                                .OrderByDescending(s => IsCriticalSensor(s.Name))
                                .ThenBy(s => s.Name.Contains("Core") || s.Name.Contains("Núcleo") ? GetCoreNumber(s.Name) : 999)
                                .ThenByDescending(s => s.Value ?? 0)
                                .Take(isCpuCard ? GetCpuSectionLimit(type) : 32)
                                .ToList();

            for (int i = 0; i < targetSensors.Count; i++)
            {
                if (i >= group.Count)
                {
                    group.Add(targetSensors[i]);
                }
                else if (group[i] != targetSensors[i])
                {
                    group[i] = targetSensors[i];
                }
            }

            while (group.Count > targetSensors.Count)
            {
                group.RemoveAt(group.Count - 1);
            }
        }

        private static int GetCpuSectionLimit(string type)
        {
            return type switch
            {
                "Load" => 16,
                "Clock" => 64, 
                "Power" => 12,
                "Temperature" => 16,
                "Voltage" => 48, 
                "Fan" => 12,
                "Current" => 12,
                _ => 24
            };
        }

        private static bool IsCriticalSensor(string name)
        {
            string n = name.ToLower();
            return n.Contains("package") || n.Contains("total") || n.Contains("tctl") ||
                   n.Contains("tdie") || n.Contains("avg") || n.Contains("max") ||
                   n.Contains("svi2") || n.Contains("tfn") ||
                   n.Contains("vcore") || n.Contains("effective") ||
                   n.Contains("smu power") || n.Contains("package power") ||
                   n.Contains("soc") || n.Contains("vid") || n.Contains("pll") ||
                   n.Contains("misc") || n.Contains("vccsa") || n.Contains("vccio") ||
                   n.Contains("vddp") || n.Contains("vddg") ||
                   n.Contains("temperatura") || n.Contains("consumo") || n.Contains("voltagem") ||
                   n.Contains("potência") || n.Contains("médio") || n.Contains("pico") ||
                   n.Contains("núcleo");
        }

        private bool IsUsefulSensor(HardwareSensor s)
        {
            if (!s.IsValid) return false;
            if (IsCriticalSensor(s.Name)) return true;
            if (s.SensorType == "Clock" || s.SensorType == "Temperature" || s.SensorType == "Power") return true;
            if (s.Value <= 0)
            {
                if (s.Name.Contains("D3D") || s.Name.Contains("Encoder") || s.Name.Contains("Decoder")) return false;
            }

            return true;
        }

        private int GetCoreNumber(string name)
        {
            var match = System.Text.RegularExpressions.Regex.Match(name, @"#(\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value) : 998;
        }

        private bool IsQuietLoadSensor(string name)
        {
            return name.Contains("D3D VR") || name.Contains("D3D Security") || name.Contains("D3D Overlay") || name.Contains("Encode");
        }

        private static bool IsReadableCpuMetric(HardwareSensor sensor, string type)
        {
            if (!sensor.Value.HasValue || sensor.Value <= 0.1f) return false;
            if (type == "Temperature")
            {
                var lowerName = sensor.Name.ToLower();
                if (lowerName.Contains("fahrenheit") || lowerName.Contains("°f")) return false;
                return sensor.Value <= 125;
            }
            if (type == "Clock") return sensor.Value >= 100;
            if (type == "Power") return sensor.Value >= 0.5f;
            return true;
        }

        private static int GetCpuSensorPriority(string type, string name)
        {
            var n = name.ToLower();
            if (type == "Temperature")
            {
                if (n.Contains("tctl") || n.Contains("tdie") || n.Contains("package")) return 4;
                if (n.Contains("average") || n.Contains("avg")) return 3;
                if (n.Contains("core max") || n.Contains("max")) return 2;
                return 1;
            }
            if (type == "Clock")
            {
                if (n.Contains("effective") || n.Contains("average") || n.Contains("core #")) return 4;
                if (n.Contains("core")) return 3;
                if (n.Contains("bus") || n.Contains("fabric")) return 1;
                return 2;
            }
            if (type == "Power")
            {
                if (n.Contains("package") || n.Contains("cpu package") || n.Contains("smu")) return 4;
                if (n.Contains("ppt") || n.Contains("core")) return 3;
                return 1;
            }
            return IsCriticalSensor(name) ? 1 : 0;
        }
    }
}
