using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Monitor_Pc.Models
{
    public partial class HardwareItem : ObservableObject
    {
        [ObservableProperty] private string name         = string.Empty;
        [ObservableProperty] private string hardwareType = string.Empty;
        [ObservableProperty] private string icon         = "";
        [ObservableProperty] private bool   isCpu;
        [ObservableProperty] private bool   isGpu;

        partial void OnHardwareTypeChanged(string value)
        {
            IsCpu = value == "Cpu";
            IsGpu = value.StartsWith("Gpu");
            Icon  = value switch
            {
                "Cpu"                                  => "\uE9D9",
                "GpuNvidia" or "GpuAmd" or "GpuIntel" => "\uE7F4",
                "Memory"                               => "\uE950",
                "Motherboard"                          => "\uECAA",
                "Storage"                              => "\uEDA2",
                "Network"                              => "\uF5DB",
                "Battery"                              => "\uE83F",
                _                                      => "\uE9CE"
            };
        }

        // ── Raw sensor list ───────────────────────────────────────────────────
        public ObservableCollection<HardwareSensor> Sensors { get; } = new();

        // ── Grouped views ─────────────────────────────────────────────────────
        [ObservableProperty] private ObservableCollection<HardwareSensor> temperatures = new();
        [ObservableProperty] private ObservableCollection<HardwareSensor> loads        = new();
        [ObservableProperty] private ObservableCollection<HardwareSensor> clocks       = new();
        [ObservableProperty] private ObservableCollection<HardwareSensor> voltages     = new();
        [ObservableProperty] private ObservableCollection<HardwareSensor> power        = new();
        [ObservableProperty] private ObservableCollection<HardwareSensor> fans         = new();
        [ObservableProperty] private ObservableCollection<HardwareSensor> current      = new();
        [ObservableProperty] private ObservableCollection<HardwareSensor> factor       = new();
        [ObservableProperty] private ObservableCollection<HardwareSensor> throughput   = new();

        [ObservableProperty] private bool hasTemperatures;
        [ObservableProperty] private bool hasLoads;
        [ObservableProperty] private bool hasClocks;
        [ObservableProperty] private bool hasVoltages;
        [ObservableProperty] private bool hasPower;
        [ObservableProperty] private bool hasFans;
        [ObservableProperty] private bool hasCurrent;
        [ObservableProperty] private bool hasFactor;
        [ObservableProperty] private bool hasThroughput;

        // ── RefreshGroups ─────────────────────────────────────────────────────

        public void RefreshGroups()
        {
            Sync(Temperatures, "Temperature");
            Sync(Loads,        "Load");
            Sync(Clocks,       "Clock");
            Sync(Voltages,     "Voltage");
            Sync(Power,        "Power");
            Sync(Fans,         "Fan");
            Sync(Current,      "Current");
            Sync(Factor,       "Factor");
            SyncMulti(Throughput, "Throughput", "Data");

            HasTemperatures = Temperatures.Count > 0;
            HasLoads        = Loads.Count        > 0;
            HasClocks       = Clocks.Count       > 0;
            HasVoltages     = Voltages.Count     > 0;
            HasPower        = Power.Count        > 0;
            HasFans         = Fans.Count         > 0;
            HasCurrent      = Current.Count      > 0;
            HasFactor       = Factor.Count       > 0;
            HasThroughput   = Throughput.Count   > 0;

            if (IsCpu) PairLoadWithClock();
        }

        // ── Pair core load → clock for dual progress bar ──────────────────────

        private void PairLoadWithClock()
        {
            foreach (var load in Loads)
            {
                if (load.LinkedSensor != null) continue;

                var m = System.Text.RegularExpressions.Regex.Match(
                    load.Name, @"#?\s*(\d+)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!m.Success) continue;

                string idx  = m.Groups[1].Value;
                var    clock = Clocks.FirstOrDefault(c =>
                    System.Text.RegularExpressions.Regex.IsMatch(
                        c.Name, @"#?\s*" + idx + @"\b",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase));

                if (clock != null) load.LinkedSensor = clock;
            }
        }

        // ── Sync helpers ──────────────────────────────────────────────────────

        private void Sync(ObservableCollection<HardwareSensor> group, string type)
        {
            var target = Sensors
                .Where(s => s.SensorType == type && s.IsValid)
                .OrderBy(s => s.Name)
                .ToList();
            Apply(group, target);
        }

        private void SyncMulti(ObservableCollection<HardwareSensor> group, params string[] types)
        {
            var target = Sensors
                .Where(s => types.Contains(s.SensorType) && s.IsValid)
                .OrderBy(s => s.Name)
                .ToList();
            Apply(group, target);
        }

        private static void Apply(ObservableCollection<HardwareSensor> group,
                                   List<HardwareSensor> target)
        {
            for (int i = 0; i < target.Count; i++)
            {
                if (i >= group.Count)           group.Add(target[i]);
                else if (group[i] != target[i]) group[i] = target[i];
            }
            while (group.Count > target.Count) group.RemoveAt(group.Count - 1);
        }
    }
}
