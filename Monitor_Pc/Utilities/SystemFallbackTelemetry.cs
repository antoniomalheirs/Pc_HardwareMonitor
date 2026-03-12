using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;

namespace Monitor_Pc.Utilities
{
    /// <summary>
    /// Reads System telemetry via Windows Performance Counters and WMI.
    /// Used when HWiNFO sensors are not available.
    /// </summary>
    public sealed class SystemFallbackTelemetry : IDisposable
    {
        // CPU
        private readonly PerformanceCounter? _cpuLoadTotal;
        private readonly PerformanceCounter? _processorFrequency;
        private readonly List<PerformanceCounter> _perCoreLoad      = new();
        private readonly List<PerformanceCounter> _perCoreFrequency = new();

        private bool _totalLoadWarmed;
        private bool _freqWarmed;
        private readonly List<bool> _perCoreLoadWarmed = new();
        private readonly List<bool> _perCoreFreqWarmed = new();

        private float _wmiClockMhz;
        private float _wmiTempCelsius;
        private float _wmiMaxClockMhz;
        private DateTime _lastCpuWmi = DateTime.MinValue;
        private readonly TimeSpan _cpuWmiInterval = TimeSpan.FromSeconds(3);

        // RAM & GPU
        private DateTime _lastRamWmi = DateTime.MinValue;
        private readonly TimeSpan _ramWmiInterval = TimeSpan.FromSeconds(2);
        private bool _gpuInitialized = false;

        public float AverageClockMhz    { get; private set; }
        public float TotalLoadPercent   { get; private set; }
        public float TemperatureCelsius { get; private set; }
        public float EstimatedWatts     { get; private set; }

        private readonly List<float> _perCoreLoadValues = new();
        private readonly List<float> _perCoreFreqValues = new();
        public IReadOnlyList<float> PerCoreLoadPercent   => _perCoreLoadValues;
        public IReadOnlyList<float> PerCoreFrequencyMhz  => _perCoreFreqValues;

        // RAM Properties
        public float RamUsagePercent { get; private set; }
        public float RamUsedGb { get; private set; }
        public float RamTotalGb { get; private set; }

        // GPU Properties
        public string GpuName { get; private set; } = "";
        public uint GpuRefreshRate { get; private set; }
        public float GpuRamGb { get; private set; }

        public SystemFallbackTelemetry()
        {
            try { _cpuLoadTotal = new PerformanceCounter("Processor", "% Processor Time", "_Total", true); } catch { }
            try { _processorFrequency = new PerformanceCounter("Processor Information", "Processor Frequency", "_Total", true); } catch { }

            // Per-core counters — "Processor Information" gives per-logical-CPU data
            try
            {
                var cat       = new PerformanceCounterCategory("Processor Information");
                var instances = cat.GetInstanceNames()
                    .Where(n => n != "_Total")
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var inst in instances)
                {
                    try { _perCoreLoad.Add(new PerformanceCounter("Processor Information", "% Processor Time", inst, true)); _perCoreLoadWarmed.Add(false); } catch { }
                    try { _perCoreFrequency.Add(new PerformanceCounter("Processor Information", "Processor Frequency", inst, true)); _perCoreFreqWarmed.Add(false); } catch { }
                }
            }
            catch { }

            _perCoreLoadValues.AddRange(Enumerable.Repeat(0f, _perCoreLoad.Count));
            _perCoreFreqValues.AddRange(Enumerable.Repeat(0f, _perCoreFrequency.Count));

            // Read max clock once for TDP estimation
            try
            {
                using var s = new ManagementObjectSearcher("SELECT MaxClockSpeed FROM Win32_Processor");
                foreach (ManagementObject mo in s.Get())
                    _wmiMaxClockMhz = Math.Max(_wmiMaxClockMhz, Convert.ToSingle(mo["MaxClockSpeed"] ?? 0f));
            }
            catch { }
        }

        public void Refresh()
        {
            // --- CPU Refresh ---
            float load = ReadCounter(_cpuLoadTotal, ref _totalLoadWarmed);
            if (load >= 0) TotalLoadPercent = load;

            float freq = ReadCounter(_processorFrequency, ref _freqWarmed);
            if (freq > 100) { AverageClockMhz = freq; _wmiClockMhz = freq; }

            for (int i = 0; i < _perCoreLoad.Count; i++)
            {
                bool w = _perCoreLoadWarmed[i];
                float v = ReadCounter(_perCoreLoad[i], ref w);
                _perCoreLoadWarmed[i] = w;
                if (v >= 0 && i < _perCoreLoadValues.Count) _perCoreLoadValues[i] = v;
            }
            for (int i = 0; i < _perCoreFrequency.Count; i++)
            {
                bool w = _perCoreFreqWarmed[i];
                float v = ReadCounter(_perCoreFrequency[i], ref w);
                _perCoreFreqWarmed[i] = w;
                if (i < _perCoreFreqValues.Count) _perCoreFreqValues[i] = v > 100 ? v : 0;
            }

            if (_perCoreFreqValues.All(v => v <= 0) && AverageClockMhz > 0)
                for (int i = 0; i < _perCoreFreqValues.Count; i++) _perCoreFreqValues[i] = AverageClockMhz;

            if (DateTime.Now - _lastCpuWmi >= _cpuWmiInterval) { _lastCpuWmi = DateTime.Now; RefreshCpuWmi(); }

            float tdp = EstimateTdp(_wmiMaxClockMhz);
            EstimatedWatts = tdp > 0 ? MathF.Max(0.5f, tdp * TotalLoadPercent / 100f) : 0;
            TemperatureCelsius = _wmiTempCelsius;

            // --- RAM Refresh ---
            if (DateTime.Now - _lastRamWmi >= _ramWmiInterval) { _lastRamWmi = DateTime.Now; RefreshRamWmi(); }

            // --- GPU Init ---
            if (!_gpuInitialized)
            {
                _gpuInitialized = true;
                InitGpuWmi();
            }
        }

        private void RefreshCpuWmi()
        {
            try
            {
                using var s = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
                float best = 0;
                foreach (ManagementObject mo in s.Get())
                {
                    float c = (Convert.ToSingle(mo["CurrentTemperature"] ?? 0f) / 10f) - 273.15f;
                    if (c >= 20 && c <= 110) best = Math.Max(best, c);
                }
                if (best > 0) _wmiTempCelsius = best;
            }
            catch { }

            if (_wmiClockMhz <= 0)
            {
                try
                {
                    using var s = new ManagementObjectSearcher("SELECT CurrentClockSpeed FROM Win32_Processor");
                    float best = 0;
                    foreach (ManagementObject mo in s.Get()) best = Math.Max(best, Convert.ToSingle(mo["CurrentClockSpeed"] ?? 0f));
                    if (best > 100) _wmiClockMhz = best;
                }
                catch { }
            }
        }

        private void RefreshRamWmi()
        {
            try
            {
                using var s = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
                foreach (ManagementObject mo in s.Get())
                {
                    ulong totalKb = Convert.ToUInt64(mo["TotalVisibleMemorySize"]);
                    ulong freeKb = Convert.ToUInt64(mo["FreePhysicalMemory"]);
                    
                    if (totalKb > 0)
                    {
                        ulong usedKb = totalKb - freeKb;
                        RamTotalGb = totalKb / 1024f / 1024f;
                        RamUsedGb = usedKb / 1024f / 1024f;
                        RamUsagePercent = (float)usedKb / totalKb * 100f;
                    }
                }
            }
            catch { }
        }

        private void InitGpuWmi()
        {
            try
            {
                using var s = new ManagementObjectSearcher("SELECT Name, AdapterRAM, CurrentRefreshRate FROM Win32_VideoController");
                foreach (ManagementObject mo in s.Get())
                {
                    string name = Convert.ToString(mo["Name"]) ?? "";
                    if (!string.IsNullOrEmpty(name))
                    {
                        GpuName = name;
                        try { GpuRefreshRate = Convert.ToUInt32(mo["CurrentRefreshRate"]); } catch { }
                        try
                        {
                            ulong ramBytes = Convert.ToUInt64(mo["AdapterRAM"]);
                            if (ramBytes > 0)
                                GpuRamGb = ramBytes / 1024f / 1024f / 1024f;
                        } catch { }
                        break;
                    }
                }
            }
            catch { }
        }

        private static float ReadCounter(PerformanceCounter? c, ref bool warmed)
        {
            if (c == null) return -1;
            try { if (!warmed) { c.NextValue(); warmed = true; return -1; } return c.NextValue(); }
            catch { return -1; }
        }

        private static float EstimateTdp(float maxMhz)
        {
            if (maxMhz >= 5500) return 170;
            if (maxMhz >= 5000) return 125;
            if (maxMhz >= 4500) return 105;
            if (maxMhz >= 4000) return  95;
            if (maxMhz >= 3200) return  65;
            if (maxMhz >= 2500) return  45;
            if (maxMhz  > 0)   return  28;
            return 65;
        }

        public void Dispose()
        {
            _cpuLoadTotal?.Dispose();
            _processorFrequency?.Dispose();
            foreach (var c in _perCoreLoad)      c.Dispose();
            foreach (var c in _perCoreFrequency) c.Dispose();
        }
    }
}
