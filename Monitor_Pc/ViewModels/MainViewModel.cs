using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LibreHardwareMonitor.Hardware;
using Monitor_Pc.Models;
using Monitor_Pc.Utilities;

namespace Monitor_Pc.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        private readonly Computer _computer;
        private readonly UpdateVisitor _updateVisitor;
        private readonly DispatcherTimer _timer;
        private readonly CpuFallbackTelemetry _cpuFallbackTelemetry;
        private readonly AmdTelemetryFix _amdFix;

        public ObservableCollection<HardwareItem> HardwareItems { get; } = new ObservableCollection<HardwareItem>();

        [ObservableProperty]
        private string maxTemp = "--";

        [ObservableProperty]
        private string ramUsage = "--";

        [ObservableProperty]
        private double ramPercentage;

        [ObservableProperty]
        private string cpuLoad = "--";

        public MainViewModel()
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true, // Enable for background sensor access
                IsControllerEnabled = true,
                IsNetworkEnabled = true,
                IsStorageEnabled = true
            };
            
            try { _computer.Open(); } catch { }
            
            // Warm-up
            _updateVisitor = new UpdateVisitor();
            for (int i = 0; i < 2; i++) 
            {
                _computer.Accept(_updateVisitor);
                System.Threading.Thread.Sleep(50);
            }

            _cpuFallbackTelemetry = new CpuFallbackTelemetry();
            _amdFix = new AmdTelemetryFix();

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += Timer_Tick;

            UpdateHardwareData();
            _timer.Start();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            UpdateHardwareData();
        }

        private void UpdateHardwareData()
        {
            try 
            {
                _computer.Accept(_updateVisitor);
                _amdFix.Refresh();

                float highestTemp = 0;
                float totalCpuLoad = 0;

                foreach (var hardware in _computer.Hardware)
                {
                    ProcessHardwareRecursive(hardware, ref highestTemp, ref totalCpuLoad);
                }

                foreach (var cpuItem in HardwareItems.Where(h => h.HardwareType == "Cpu"))
                {
                    ApplyCpuFallbackSensors(cpuItem);
                    cpuItem.RefreshGroups();
                }

                if (highestTemp > 0) MaxTemp = $"{highestTemp:F0}°";
                if (totalCpuLoad > 0) CpuLoad = $"{totalCpuLoad:F0}%";
            }
            catch { /* Maintain stability */ }
        }

        private void ProcessHardwareRecursive(IHardware hardware, ref float highestTemp, ref float totalCpuLoad)
        {
            // STRICT UI FILTERING: Only CPU and GPU allowed in the dashboard
            bool isCpu = hardware.HardwareType == HardwareType.Cpu;
            bool isGpu = hardware.HardwareType.ToString().Contains("Gpu");

            if (isCpu || isGpu)
            {
                var targetItem = HardwareItems.FirstOrDefault(h => h.Name == hardware.Name || (isCpu && h.HardwareType == "Cpu"));
                
                if (targetItem == null)
                {
                    targetItem = new HardwareItem
                    {
                        Name = hardware.Name,
                        HardwareType = isCpu ? "Cpu" : hardware.HardwareType.ToString(),
                        IsCpu = isCpu,
                        IsGpu = isGpu
                    };
                    HardwareItems.Add(targetItem);
                }

                UpdateSensors(targetItem, hardware.Sensors);
                foreach (var sub in hardware.SubHardware) UpdateSensors(targetItem, sub.Sensors);
                
                targetItem.RefreshGroups();
            }

            // Summary data collection
            foreach (var sensor in hardware.Sensors)
            {
                if (!sensor.Value.HasValue || sensor.Value <= 0) continue;
                if (sensor.SensorType == SensorType.Temperature && IsPreferredCpuTemperature(sensor.Name))
                    highestTemp = Math.Max(highestTemp, sensor.Value.Value);
                if (sensor.SensorType == SensorType.Load && IsPreferredCpuLoad(sensor.Name))
                    totalCpuLoad = Math.Max(totalCpuLoad, sensor.Value.Value);
            }

            foreach (var subHardware in hardware.SubHardware)
                ProcessHardwareRecursive(subHardware, ref highestTemp, ref totalCpuLoad);
        }

        private void UpdateSensors(HardwareItem item, ISensor[] sensors)
        {
            foreach (var sensor in sensors)
            {
                var sensorType = sensor.SensorType.ToString();
                var sensorId = sensor.Identifier.ToString();

                var existing = item.Sensors.FirstOrDefault(s =>
                    (!string.IsNullOrWhiteSpace(s.SensorId) && s.SensorId == sensorId) ||
                    (string.IsNullOrWhiteSpace(s.SensorId) && s.Name == sensor.Name && s.SensorType == sensorType));

                if (existing != null)
                {
                    existing.Name = sensor.Name;
                    existing.SensorType = sensorType;
                    existing.SensorId = sensorId;
                    existing.Value = sensor.Value;
                    continue;
                }

                item.Sensors.Add(new HardwareSensor
                {
                    Name = sensor.Name,
                    SensorType = sensorType,
                    SensorId = sensorId,
                    Value = sensor.Value
                });
            }
        }

        private void ApplyCpuFallbackSensors(HardwareItem cpuItem)
        {
            UpsertCpuFallbackSensor(cpuItem, "CPU Package (Fallback)", "Temperature", _cpuFallbackTelemetry.TryReadTemperature);
            UpsertCpuFallbackSensor(cpuItem, "CPU Effective Clock (Fallback)", "Clock", _cpuFallbackTelemetry.TryReadClockMhz);
            UpsertCpuFallbackSensor(cpuItem, "CPU Package Power (Fallback)", "Power", _cpuFallbackTelemetry.TryReadPackagePower);
            
            // AMD Fix Fallbacks
            var amdTemp = _amdFix.GetCpuTemp();
            if (amdTemp.HasValue) UpsertCpuFallbackSensor(cpuItem, "CPU Temp (AMD Fix)", "Temperature", out _ , amdTemp.Value);

            var amdClock = _amdFix.GetAverageClock();
            if (amdClock.HasValue) UpsertCpuFallbackSensor(cpuItem, "CPU Clock (AMD Fix)", "Clock", out _ , amdClock.Value);
        }

        private static void UpsertCpuFallbackSensor(HardwareItem cpuItem, string sensorName, string sensorType, TryReadMetric readMetric)
        {
            if (!readMetric(out var value) || value <= 0) return;
            UpsertCpuFallbackSensor(cpuItem, sensorName, sensorType, out _, value);
        }

        private static void UpsertCpuFallbackSensor(HardwareItem cpuItem, string sensorName, string sensorType, out float val, float value)
        {
            val = value;
            var fallbackId = $"fallback::{sensorType}::{sensorName}";
            var sensor = cpuItem.Sensors.FirstOrDefault(s => s.SensorId == fallbackId);
            if (sensor == null)
            {
                cpuItem.Sensors.Add(new HardwareSensor
                {
                    Name = sensorName,
                    SensorType = sensorType,
                    SensorId = fallbackId,
                    Value = value
                });
                return;
            }

            sensor.Value = value;
        }

        private static bool IsPreferredCpuTemperature(string sensorName)
        {
            return sensorName.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                   sensorName.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
                   sensorName.Contains("Average", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPreferredCpuLoad(string sensorName)
        {
            return sensorName.Contains("Total", StringComparison.OrdinalIgnoreCase) ||
                   sensorName.Contains("Package", StringComparison.OrdinalIgnoreCase);
        }

        public void Dispose()
        {
            _timer.Stop();
            _cpuFallbackTelemetry.Dispose();
            _computer.Close();
        }

        private delegate bool TryReadMetric(out float value);
    }
}
