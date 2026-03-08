using System;
using System.Diagnostics;
using System.Linq;
using System.Management;

namespace Monitor_Pc.Utilities
{
    public sealed class CpuFallbackTelemetry : IDisposable
    {
        private readonly PerformanceCounter? _cpuLoadCounter;
        private readonly PerformanceCounter? _cpuClockCounter;

        private bool _loadCounterWarm;
        private bool _clockCounterWarm;

        public CpuFallbackTelemetry()
        {
            try
            {
                _cpuLoadCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", readOnly: true);
            }
            catch
            {
                _cpuLoadCounter = null;
            }

            try
            {
                _cpuClockCounter = new PerformanceCounter("Processor Information", "Processor Frequency", "_Total", readOnly: true);
            }
            catch
            {
                _cpuClockCounter = null;
            }
        }

        public bool TryReadClockMhz(out float clockMhz)
        {
            clockMhz = 0;

            var counterClock = ReadCounter(_cpuClockCounter, ref _clockCounterWarm);
            if (counterClock > 100)
            {
                clockMhz = counterClock;
                return true;
            }

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT CurrentClockSpeed FROM Win32_Processor");
                var maxClock = searcher.Get()
                    .Cast<ManagementObject>()
                    .Select(mo => Convert.ToSingle(mo["CurrentClockSpeed"] ?? 0f))
                    .DefaultIfEmpty()
                    .Max();

                if (maxClock > 100)
                {
                    clockMhz = maxClock;
                    return true;
                }
            }
            catch
            {
                // ignored: fallback is optional
            }

            return false;
        }

        public bool TryReadPackagePower(out float watts)
        {
            watts = 0;

            var cpuLoad = ReadCounter(_cpuLoadCounter, ref _loadCounterWarm);
            if (cpuLoad <= 0)
            {
                return false;
            }

            // Conservative estimation fallback when hardware API power sensors are unavailable.
            var estimatedTdp = ReadEstimatedTdpFromWmi();
            if (estimatedTdp <= 0)
            {
                estimatedTdp = 65;
            }

            watts = MathF.Max(0.1f, estimatedTdp * (cpuLoad / 100f));
            return true;
        }

        public bool TryReadTemperature(out float celsius)
        {
            celsius = 0;

            try
            {
                using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
                var temp = searcher.Get()
                    .Cast<ManagementObject>()
                    .Select(mo => Convert.ToSingle(mo["CurrentTemperature"] ?? 0f))
                    .Where(v => v > 0)
                    .Select(v => (v / 10f) - 273.15f)
                    .DefaultIfEmpty()
                    .Average();

                if (temp > 0 && temp < 125)
                {
                    celsius = temp;
                    return true;
                }
            }
            catch
            {
                // ignored: fallback is optional
            }

            return false;
        }

        private static float ReadCounter(PerformanceCounter? counter, ref bool warmed)
        {
            if (counter == null)
            {
                return 0;
            }

            try
            {
                if (!warmed)
                {
                    counter.NextValue();
                    warmed = true;
                    return 0;
                }

                return counter.NextValue();
            }
            catch
            {
                return 0;
            }
        }

        private static float ReadEstimatedTdpFromWmi()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT MaxClockSpeed FROM Win32_Processor");
                var maxClock = searcher.Get()
                    .Cast<ManagementObject>()
                    .Select(mo => Convert.ToSingle(mo["MaxClockSpeed"] ?? 0f))
                    .DefaultIfEmpty()
                    .Average();

                if (maxClock >= 5000) return 125;
                if (maxClock >= 4000) return 95;
                if (maxClock >= 3200) return 65;
                if (maxClock >= 2500) return 45;
                if (maxClock > 0) return 28;
            }
            catch
            {
                // ignored: fallback is optional
            }

            return 0;
        }

        public void Dispose()
        {
            _cpuLoadCounter?.Dispose();
            _cpuClockCounter?.Dispose();
        }
    }
}
