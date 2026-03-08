using System.Collections.ObjectModel;
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

        partial void OnHardwareTypeChanged(string value)
        {
            IsCpu = value == "Cpu";

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

        public void RefreshGroups()
        {
            UpdateGroup(Temperatures, "Temperature");
            UpdateGroup(Loads, "Load");
            UpdateGroup(Clocks, "Clock");
            UpdateGroup(Voltages, "Voltage");
            UpdateGroup(Power, "Power");
            UpdateGroup(Fans, "Fan");

            HasTemperatures = Temperatures.Count > 0;
            HasLoads = Loads.Count > 0;
            HasClocks = Clocks.Count > 0;
            HasVoltages = Voltages.Count > 0;
            HasPower = Power.Count > 0;
            HasFans = Fans.Count > 0;

            if (IsCpu)
            {
                RefreshCpuOverview();
            }
        }

        private void RefreshCpuOverview()
        {
            var preferredLoad = PickPreferredSensor(Loads,
                "Total CPU", "CPU Total", "Total", "Package", "Core Max");
            CpuTotalLoad = preferredLoad?.FormattedValue ?? "--";
            CpuTotalLoadPercentage = preferredLoad?.ValuePercentage ?? 0;

            var preferredTemp = PickPreferredSensor(Temperatures,
                "Package", "Tctl", "Tdie", "Core (Tctl/Tdie)", "Average");
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
                "Package", "SMU", "Core", "PPT", "CPU");
            CpuPackagePower = preferredPower?.FormattedValue ?? "--";

            var preferredVoltage = PickPreferredSensor(Voltages,
                "Vcore", "Core", "SVI2", "CPU", "VID");
            CpuCoreVoltage = preferredVoltage?.FormattedValue ?? "--";
        }

        private static HardwareSensor? PickPreferredSensor(IEnumerable<HardwareSensor> source, params string[] keywords)
        {
            return source
                .Where(s => s.IsValid && s.Value > 0)
                .OrderByDescending(s => keywords.Any(k => s.Name.Contains(k, System.StringComparison.OrdinalIgnoreCase)))
                .ThenByDescending(s => s.Value)
                .FirstOrDefault();
        }

        private void UpdateGroup(ObservableCollection<HardwareSensor> group, string type)
        {
            // Strict filter: Hide nulls and junk/idle data
            var query = Sensors.Where(s => s.SensorType == type && s.IsValid && IsUsefulSensor(s));
            bool isCpuCard = HardwareType == "Cpu";

            // Restoration & Noise Filtering
            if (type == "Load")
            {
                query = query.Where(s => s.Value > 0 || !IsQuietLoadSensor(s.Name));
            }
            if (type == "Fan")
            {
                query = query.Where(s => s.Value > 0 || s.Name.Contains("CPU"));
            }
            // For Voltages, we only want VCore and essentials if there are many
            if (type == "Voltage")
            {
                query = query.Where(s => IsCriticalSensor(s.Name) || (s.Value > 0 && !s.Name.Contains("VID")));
            }

            if (isCpuCard && (type == "Clock" || type == "Temperature" || type == "Power"))
            {
                query = query.Where(s => IsReadableCpuMetric(s, type));
            }

            var targetSensors = query
                                .OrderByDescending(s => isCpuCard ? GetCpuSensorPriority(type, s.Name) : (IsCriticalSensor(s.Name) ? 1 : 0))
                                .ThenBy(s => s.Name.Contains("Core") ? GetCoreNumber(s.Name) : 999) 
                                .OrderByDescending(s => IsCriticalSensor(s.Name))
                                .ThenBy(s => s.Name.Contains("Core") ? GetCoreNumber(s.Name) : 999)
                                .ThenByDescending(s => type == "Temperature" || type == "Load" || type == "Clock" ? s.Value : 0)
                                .ToList();

            // sync items
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

        private static bool IsCriticalSensor(string name)
        {
            string n = name.ToLower();
            return n.Contains("package") || n.Contains("total") || n.Contains("tctl") ||
                   n.Contains("tdie") || n.Contains("avg") || n.Contains("max") ||
                   n.Contains("svi2") || n.Contains("vcore") || n.Contains("effective") ||
                   n.Contains("smu power") || n.Contains("package power");
        }

        private bool IsUsefulSensor(HardwareSensor s)
        {
            if (!s.IsValid) return false;

            // Critical sensors always stay visible to confirm they exist
            if (IsCriticalSensor(s.Name)) return true;

            // Clocks/Temps/Power are essentially critical for Ryzen stability monitoring
            if (s.SensorType == "Clock" || s.SensorType == "Temperature" || s.SensorType == "Power") return true;

            // Hide other per-core items that are idle/zero
            if (s.Value <= 0)
            {
                if (s.Name.Contains("D3D") || s.Name.Contains("Encoder") || s.Name.Contains("Decoder")) return false;
            }

            return true;
        }

        private int GetCoreNumber(string name)
        {
            // Extract number from "Core #1", "CPU Core #1", etc.
            var match = System.Text.RegularExpressions.Regex.Match(name, @"#(\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value) : 998;
        }

        private bool IsQuietLoadSensor(string name)
        {
            // Only purely peripheral engines
            return name.Contains("D3D VR") || name.Contains("D3D Security") || name.Contains("D3D Overlay") || name.Contains("Encode");
        }

        private static bool IsReadableCpuMetric(HardwareSensor sensor, string type)
        {
            if (!sensor.Value.HasValue || sensor.Value <= 0.1f)
            {
                return false;
            }

            if (type == "Temperature")
            {
                var lowerName = sensor.Name.ToLower();
                if (lowerName.Contains("fahrenheit") || lowerName.Contains("°f") || lowerName.Contains("farenheit"))
                {
                    return false;
                }

                return sensor.Value <= 125;
            }

            if (type == "Clock")
            {
                return sensor.Value >= 100;
            }

            if (type == "Power")
            {
                return sensor.Value >= 0.5f;
            }

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
