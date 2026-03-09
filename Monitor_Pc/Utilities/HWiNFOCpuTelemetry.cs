using System;
using System.Collections.Generic;
using System.Linq;

namespace Monitor_Pc.Utilities
{
    /// <summary>
    /// High-level facade that maps HWiNFO64 shared memory readings
    /// to strongly-typed CPU sensor values used by the app.
    ///
    /// Call <see cref="Refresh"/> once per timer tick.
    /// Check <see cref="IsAvailable"/> before using values.
    /// </summary>
    public sealed class HWiNFOCpuTelemetry : IDisposable
    {
        private readonly HWiNFOReader _reader = new();

        // ── CPU identification ────────────────────────────────────────────────
        // HWiNFO names the CPU sensor something like "AMD Ryzen 9 7950X3D [CCD0 ...]"
        // We search for sensors whose name contains any of these keywords:
        private static readonly string[] CpuSensorKeywords =
            { "Ryzen", "Core i", "Intel", "Xeon", "Threadripper", "EPYC", "Athlon", "CPU [" };

        // ── Cached results ─────────────────────────────────────────────────────
        public bool   IsAvailable        { get; private set; }

        // Temperatures
        public double TctlTdie           { get; private set; }   // °C  – Tctl/Tdie
        public double CcdMax             { get; private set; }   // °C  – hottest CCD
        public IReadOnlyList<double> CcdTemperatures { get; private set; } = Array.Empty<double>();

        // Clocks
        public double AverageEffClock    { get; private set; }   // MHz
        public IReadOnlyList<(string Name, double Mhz)> CoreClocks { get; private set; }
            = Array.Empty<(string, double)>();

        // Voltages
        public double CoreVoltage        { get; private set; }   // V  – Vcore / SVI2 Core
        public double SocVoltage         { get; private set; }   // V  – VSOC  / SVI2 SoC
        public double VddpVoltage        { get; private set; }   // V
        public double VddgVoltage        { get; private set; }   // V
        public IReadOnlyList<(string Name, double Volts)> CpuVoltages { get; private set; }
            = Array.Empty<(string, double)>();

        // Power
        public double PackagePower       { get; private set; }   // W  – PPT / Package
        public double CorePower          { get; private set; }   // W
        public double SocPower           { get; private set; }   // W
        public IReadOnlyList<(string Name, double Watts)> CorePowers { get; private set; }
            = Array.Empty<(string, double)>();

        // Current / EDC / TDC
        public double TdcCurrentA        { get; private set; }   // A
        public double EdcCurrentA        { get; private set; }   // A

        // Load
        public double TotalLoad          { get; private set; }   // %
        public IReadOnlyList<(string Name, double Percent)> CoreLoads { get; private set; }
            = Array.Empty<(string, double)>();

        // Fabric / Memory
        public double FclkMhz            { get; private set; }
        public double UclkMhz            { get; private set; }
        public double MclkMhz            { get; private set; }

        // ── Refresh ────────────────────────────────────────────────────────────

        public void Refresh()
        {
            if (!_reader.Refresh())
            {
                IsAvailable = false;
                return;
            }

            IsAvailable = true;
            ParseAll();
        }

        // ── Parsing ────────────────────────────────────────────────────────────

        private void ParseAll()
        {
            // Collect all readings that belong to a CPU sensor
            var cpuReadings = _reader.AllReadings
                .Where(r => IsCpuSensor(r.SensorName))
                .ToList();

            if (cpuReadings.Count == 0)
            {
                // Try a broader match — use ALL readings and filter by label keywords
                cpuReadings = _reader.AllReadings.ToList();
            }

            ParseTemperatures(cpuReadings);
            ParseClocks(cpuReadings);
            ParseVoltages(cpuReadings);
            ParsePower(cpuReadings);
            ParseCurrents(cpuReadings);
            ParseLoads(cpuReadings);
            ParseFabric(cpuReadings);
        }

        private void ParseTemperatures(List<HWiNFO_Reading> r)
        {
            var temps = r.Where(x => x.Type == SENSOR_READING_TYPE.SENSOR_TYPE_TEMP).ToList();

            // Tctl / Tdie
            var tctl = temps.FirstOrDefault(x =>
                x.Label.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
                x.Label.Contains("Tdie", StringComparison.OrdinalIgnoreCase) ||
                x.Label.Equals("CPU (Tctl/Tdie)", StringComparison.OrdinalIgnoreCase));
            if (tctl != null) TctlTdie = tctl.Value;

            // CCD temps (Zen2+)
            var ccds = temps
                .Where(x => x.Label.Contains("CCD", StringComparison.OrdinalIgnoreCase) ||
                            x.Label.Contains("Die ", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Label)
                .Select(x => x.Value)
                .ToList();
            CcdTemperatures = ccds;
            CcdMax = ccds.Count > 0 ? ccds.Max() : 0;
        }

        private void ParseClocks(List<HWiNFO_Reading> r)
        {
            var clocks = r.Where(x => x.Type == SENSOR_READING_TYPE.SENSOR_TYPE_CLOCK).ToList();

            // Per-core effective clocks (HWiNFO labels them "Core #0 T#0", "Core 0 Speed", etc.)
            var coreClocks = clocks
                .Where(x =>
                    x.Label.Contains("Core",      StringComparison.OrdinalIgnoreCase) &&
                   (x.Label.Contains("Effective", StringComparison.OrdinalIgnoreCase) ||
                    x.Label.Contains("Speed",     StringComparison.OrdinalIgnoreCase) ||
                    x.Label.Contains("Clock",     StringComparison.OrdinalIgnoreCase) ||
                    System.Text.RegularExpressions.Regex.IsMatch(x.Label, @"Core\s*#?\d+")))
                .Where(x => x.Value > 100)
                .OrderBy(x => x.Label)
                .Select(x => (x.Label, x.Value))
                .ToList();

            CoreClocks = coreClocks;

            // Average effective clock
            if (coreClocks.Count > 0)
                AverageEffClock = coreClocks.Average(c => c.Value);
            else
            {
                // Try "CPU Clock" or "Effective Clock" labels
                var avg = clocks.FirstOrDefault(x =>
                    x.Label.Contains("Effective", StringComparison.OrdinalIgnoreCase) ||
                    (x.Label.Contains("CPU",  StringComparison.OrdinalIgnoreCase) &&
                     x.Label.Contains("Clock", StringComparison.OrdinalIgnoreCase)));
                if (avg != null) AverageEffClock = avg.Value;
            }

            // Fabric / Memory
            var fclk = clocks.FirstOrDefault(x => x.Label.Contains("FCLK", StringComparison.OrdinalIgnoreCase));
            if (fclk != null) FclkMhz = fclk.Value;

            var uclk = clocks.FirstOrDefault(x => x.Label.Contains("UCLK", StringComparison.OrdinalIgnoreCase));
            if (uclk != null) UclkMhz = uclk.Value;

            var mclk = clocks.FirstOrDefault(x => x.Label.Contains("MCLK", StringComparison.OrdinalIgnoreCase) ||
                                                   x.Label.Contains("Mem Clock", StringComparison.OrdinalIgnoreCase));
            if (mclk != null) MclkMhz = mclk.Value;
        }

        private void ParseVoltages(List<HWiNFO_Reading> r)
        {
            var volts = r.Where(x => x.Type == SENSOR_READING_TYPE.SENSOR_TYPE_VOLT).ToList();

            var cpuVoltages = volts
                .Where(IsCpuVoltageReading)
                .OrderBy(x => x.Label)
                .Select(x => ($"{x.SensorName} / {x.Label}", x.Value))
                .ToList();
            CpuVoltages = cpuVoltages;

            var prioritizedVolts = cpuVoltages.Count > 0
                ? volts.Where(IsCpuVoltageReading).ToList()
                : volts;

            // Core voltage — SVI2 TFN Core, Vcore, CPU VDD, etc.
            var coreV = prioritizedVolts.FirstOrDefault(x =>
                (x.Label.Contains("Core",  StringComparison.OrdinalIgnoreCase) &&
                 x.Label.Contains("SVI2",  StringComparison.OrdinalIgnoreCase)) ||
                x.Label.Equals("CPU Core Voltage",  StringComparison.OrdinalIgnoreCase) ||
                x.Label.Equals("Vcore",             StringComparison.OrdinalIgnoreCase) ||
                x.Label.Contains("CPU VDD",         StringComparison.OrdinalIgnoreCase));

            // Fallback: any "Core" voltage that isn't static
            coreV ??= prioritizedVolts.FirstOrDefault(x =>
                x.Label.Contains("Core", StringComparison.OrdinalIgnoreCase) &&
                x.Value > 0.1 && x.Value < 1.6 &&
                Math.Abs(x.Value - 1.55) > 0.01);

            if (coreV != null) CoreVoltage = coreV.Value;

            // SoC voltage
            var socV = prioritizedVolts.FirstOrDefault(x =>
                x.Label.Contains("SoC",  StringComparison.OrdinalIgnoreCase) ||
                x.Label.Contains("VSOC", StringComparison.OrdinalIgnoreCase) ||
                x.Label.Contains("VDDCR_SOC", StringComparison.OrdinalIgnoreCase));
            if (socV != null) SocVoltage = socV.Value;

            // VDDP
            var vddp = prioritizedVolts.FirstOrDefault(x => x.Label.Contains("VDDP", StringComparison.OrdinalIgnoreCase));
            if (vddp != null) VddpVoltage = vddp.Value;

            // VDDG
            var vddg = prioritizedVolts.FirstOrDefault(x => x.Label.Contains("VDDG", StringComparison.OrdinalIgnoreCase));
            if (vddg != null) VddgVoltage = vddg.Value;
        }

        private void ParsePower(List<HWiNFO_Reading> r)
        {
            var power = r.Where(x => x.Type == SENSOR_READING_TYPE.SENSOR_TYPE_POWER).ToList();

            // Package / PPT — highest priority
            var pkg = power.FirstOrDefault(x =>
                x.Label.Equals("CPU Package Power", StringComparison.OrdinalIgnoreCase) ||
                x.Label.Equals("Package Power",     StringComparison.OrdinalIgnoreCase) ||
                x.Label.Contains("PPT",             StringComparison.OrdinalIgnoreCase));
            if (pkg != null) PackagePower = pkg.Value;

            // Core power
            var coreP = power.FirstOrDefault(x =>
                x.Label.Equals("Core Power",     StringComparison.OrdinalIgnoreCase) ||
                x.Label.Equals("CPU Core Power", StringComparison.OrdinalIgnoreCase));
            if (coreP != null) CorePower = coreP.Value;

            // SoC power
            var socP = power.FirstOrDefault(x =>
                x.Label.Contains("SoC",  StringComparison.OrdinalIgnoreCase) &&
                x.Type == SENSOR_READING_TYPE.SENSOR_TYPE_POWER);
            if (socP != null) SocPower = socP.Value;

            // Per-core SMU powers
            var perCore = power
                .Where(x => x.Label.Contains("Core #", StringComparison.OrdinalIgnoreCase) ||
                            System.Text.RegularExpressions.Regex.IsMatch(x.Label, @"Core\s*#?\d+.*Power"))
                .OrderBy(x => x.Label)
                .Select(x => (x.Label, x.Value))
                .ToList();
            CorePowers = perCore;
        }

        private void ParseCurrents(List<HWiNFO_Reading> r)
        {
            // HWiNFO reports TDC/EDC under "Other" type with label containing "TDC"/"EDC"
            // or sometimes under Power with unit "A"
            var others = r.Where(x =>
                x.Type == SENSOR_READING_TYPE.SENSOR_TYPE_OTHER ||
                x.Type == SENSOR_READING_TYPE.SENSOR_TYPE_POWER).ToList();

            var tdc = others.FirstOrDefault(x =>
                x.Label.Contains("TDC", StringComparison.OrdinalIgnoreCase));
            if (tdc != null) TdcCurrentA = tdc.Value;

            var edc = others.FirstOrDefault(x =>
                x.Label.Contains("EDC", StringComparison.OrdinalIgnoreCase));
            if (edc != null) EdcCurrentA = edc.Value;
        }

        private void ParseLoads(List<HWiNFO_Reading> r)
        {
            var usage = r.Where(x => x.Type == SENSOR_READING_TYPE.SENSOR_TYPE_USAGE).ToList();

            // Total CPU load
            var total = usage.FirstOrDefault(x =>
                x.Label.Equals("Total CPU Usage",    StringComparison.OrdinalIgnoreCase) ||
                x.Label.Equals("CPU Usage",          StringComparison.OrdinalIgnoreCase) ||
                x.Label.Contains("Total",            StringComparison.OrdinalIgnoreCase));
            if (total != null) TotalLoad = total.Value;

            // Per-core loads
            var perCore = usage
                .Where(x =>
                    x.Label.Contains("Core",      StringComparison.OrdinalIgnoreCase) &&
                   (x.Label.Contains("Usage",     StringComparison.OrdinalIgnoreCase) ||
                    x.Label.Contains("T#",        StringComparison.OrdinalIgnoreCase) ||
                    System.Text.RegularExpressions.Regex.IsMatch(x.Label, @"Core\s*#?\d+")))
                .OrderBy(x => x.Label)
                .Select(x => (x.Label, x.Value))
                .ToList();
            CoreLoads = perCore;
        }

        private void ParseFabric(List<HWiNFO_Reading> r)
        {
            // Already done in ParseClocks, but some HWiNFO versions report
            // FCLK under "Other" type
            if (FclkMhz <= 0)
            {
                var fclk = r.FirstOrDefault(x =>
                    x.Label.Contains("FCLK", StringComparison.OrdinalIgnoreCase) ||
                    x.Label.Contains("Fabric", StringComparison.OrdinalIgnoreCase));
                if (fclk != null) FclkMhz = fclk.Value;
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static bool IsCpuSensor(string name)
        {
            foreach (var kw in CpuSensorKeywords)
                if (name.Contains(kw, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool IsCpuVoltageReading(HWiNFO_Reading reading)
        {
            if (!reading.Unit.Contains("V", StringComparison.OrdinalIgnoreCase)) return false;

            if (IsCpuSensor(reading.SensorName)) return true;

            string label = reading.Label;
            return
                label.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("Vcore", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("SVI", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("VID", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("SoC", StringComparison.OrdinalIgnoreCase) ||
                label.Contains("VDD", StringComparison.OrdinalIgnoreCase);
        }

        public void Dispose() => _reader.Dispose();
    }
}
