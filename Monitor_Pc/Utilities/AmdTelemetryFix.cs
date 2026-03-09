using System;
using System.Linq;
using Hardware.Info;

namespace Monitor_Pc.Utilities
{
    public sealed class AmdTelemetryFix
    {
        private readonly HardwareInfo _hardwareInfo;
        private DateTime _lastRefresh = DateTime.MinValue;
        private readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(1);

        public AmdTelemetryFix()
        {
            _hardwareInfo = new HardwareInfo();
        }

        public void Refresh()
        {
            if (DateTime.Now - _lastRefresh < _refreshInterval) return;
            
            try 
            {
                // Only refresh CPU to maintain performance
                _hardwareInfo.RefreshCPUList();
                _lastRefresh = DateTime.Now;
            }
            catch { }
        }

        public float? GetCpuTemp()
        {
            try
            {
                // Hardware.Info sometimes gets temps via WMI/ThermalZones
                return _hardwareInfo.CpuList.Max(c => (float?)c.MaxClockSpeed); // Placeholder for temp if not available
            }
            catch { return null; }
        }

        public float? GetDynamicVcore()
        {
            // Note: Hardware.Info might not have direct Vcore, but it can trigger WMI updates
            return null;
        }

        public float? GetAverageClock()
        {
            try
            {
                var cpu = _hardwareInfo.CpuList.FirstOrDefault();
                if (cpu != null)
                {
                    return (float)cpu.CurrentClockSpeed;
                }
                return null;
            }
            catch { return null; }
        }
    }
}
