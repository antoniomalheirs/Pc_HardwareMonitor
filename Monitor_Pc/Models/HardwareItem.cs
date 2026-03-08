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

        partial void OnHardwareTypeChanged(string value)
        {
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
        }

        private void UpdateGroup(ObservableCollection<HardwareSensor> group, string type)
        {
            // Strict filter: Hide nulls and junk/idle data
            var query = Sensors.Where(s => s.SensorType == type && s.IsValid && IsUsefulSensor(s));

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

            var targetSensors = query
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

        private bool IsCriticalSensor(string name)
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
    }
}
