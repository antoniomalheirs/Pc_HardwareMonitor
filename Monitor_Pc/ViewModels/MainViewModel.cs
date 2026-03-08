using System;
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
                IsMotherboardEnabled = true,
                IsControllerEnabled = true,
                IsNetworkEnabled = true,
                IsStorageEnabled = true,
                IsPsuEnabled = true // Enable all for maximum driver probing
            };
            
            _computer.Open();
            // Exhaustive Warm-up: Many Zen 3 sensors need several cycles to reach stable state
            for (int i = 0; i < 5; i++) 
            {
                _computer.Accept(new UpdateVisitor());
                System.Threading.Thread.Sleep(100); // Tiny pause between warm-up polls
            }

            _updateVisitor = new UpdateVisitor();
            _cpuFallbackTelemetry = new CpuFallbackTelemetry();

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
            _computer.Accept(_updateVisitor);

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

            // Summary priority: ONLY update if we found non-zero values
            if (highestTemp > 0) MaxTemp = $"{highestTemp:F0}°";
            if (totalCpuLoad > 0) CpuLoad = $"{totalCpuLoad:F0}%";
        }

        private void ProcessHardwareRecursive(IHardware hardware, ref float highestTemp, ref float totalCpuLoad)
        {
            bool isMotherboard = hardware.HardwareType == HardwareType.Motherboard;
            bool isTargetCard = hardware.HardwareType == HardwareType.Cpu || 
                                hardware.HardwareType.ToString().Contains("Gpu");

            if (isTargetCard || isMotherboard)
            {
                HardwareItem targetItem;
                
                if (isMotherboard)
                {
                    targetItem = HardwareItems.FirstOrDefault(h => h.HardwareType == "Cpu");
                }
                else
                {
                    // For CPU/GPU, we use the specific hardware name
                    var existingItem = HardwareItems.FirstOrDefault(h => h.Name == hardware.Name);
                    if (existingItem == null)
                    {
                        existingItem = new HardwareItem
                        {
                            Name = hardware.Name,
                            HardwareType = hardware.HardwareType.ToString()
                        };
                        HardwareItems.Add(existingItem);
                    }
                    targetItem = existingItem;
                }

                if (targetItem != null)
                {
                    UpdateSensors(targetItem, hardware.Sensors, isMotherboard);
                    
                    foreach (var sub in hardware.SubHardware)
                    {
                        UpdateSensors(targetItem, sub.Sensors, isMotherboard);
                    }

                    targetItem.RefreshGroups();
                }
            }

            // Summary data collection
            foreach (var sensor in hardware.Sensors)
            {
                if (sensor.SensorType == SensorType.Temperature)
                {
                    // Prioritize Package/Tctl for Ryzen
                    if (sensor.Value > 0 && (sensor.Name.Contains("Package") || sensor.Name.Contains("Tctl") || 
                        sensor.Name.Contains("Avg") || sensor.Name == "Core (Tctl/Tdie)"))
                    {
                         if (sensor.Value > highestTemp) highestTemp = sensor.Value ?? 0;
                    }
                }
                
                if (sensor.SensorType == SensorType.Load)
                {
                    if (sensor.Value > 0 && (sensor.Name.Contains("Total") || sensor.Name.Contains("Package") || 
                        sensor.Name == "CPU Core Max" || sensor.Name == "GPU Core"))
                    {
                        if (sensor.Value > totalCpuLoad) totalCpuLoad = sensor.Value ?? 0;
                    }
                }
            }

            foreach (var subHardware in hardware.SubHardware)
            {
                ProcessHardwareRecursive(subHardware, ref highestTemp, ref totalCpuLoad);
            }
        }

        private bool IsTargetHardware(string type)
        {
            return type == "Cpu" || type.Contains("Gpu");
        }


        private void ApplyCpuFallbackSensors(HardwareItem cpuItem)
        {
            bool hasClock = cpuItem.Sensors.Any(s => s.SensorType == SensorType.Clock.ToString() && s.IsValid && s.Value > 100);
            bool hasPower = cpuItem.Sensors.Any(s => s.SensorType == SensorType.Power.ToString() && s.IsValid && s.Value > 0.5f);
            bool hasTemp = cpuItem.Sensors.Any(s => s.SensorType == SensorType.Temperature.ToString() && s.IsValid && s.Value > 1f);

            if (!hasClock && _cpuFallbackTelemetry.TryReadClockMhz(out var clockMhz))
            {
                UpsertFallbackSensor(cpuItem, "CPU Clock (Fallback)", SensorType.Clock, clockMhz);
            }

            if (!hasPower && _cpuFallbackTelemetry.TryReadPackagePower(out var watts))
            {
                UpsertFallbackSensor(cpuItem, "CPU Package Power (Estimated)", SensorType.Power, watts);
            }

            if (!hasTemp && _cpuFallbackTelemetry.TryReadTemperature(out var tempC))
            {
                UpsertFallbackSensor(cpuItem, "CPU Temperature (Fallback)", SensorType.Temperature, tempC);
            }
        }

        private static void UpsertFallbackSensor(HardwareItem item, string name, SensorType sensorType, float value)
        {
            var sensorTypeName = sensorType.ToString();
            var existing = item.Sensors.FirstOrDefault(s => s.Name == name && s.SensorType == sensorTypeName);
            if (existing == null)
            {
                existing = new HardwareSensor
                {
                    Name = name,
                    SensorType = sensorTypeName,
                    IsMotherboardSource = false
                };
                item.Sensors.Add(existing);
            }

            existing.Value = value;
        }

        private void UpdateSensors(HardwareItem item, ISensor[] sensors, bool fromMotherboard)
        {
            var filteredSensors = sensors.Where(s => 
                s.SensorType == SensorType.Temperature || 
                s.SensorType == SensorType.Voltage ||
                s.SensorType == SensorType.Clock ||
                s.SensorType == SensorType.Load ||
                s.SensorType == SensorType.Power ||
                s.SensorType == SensorType.Fan).ToList();

            foreach (var sensor in filteredSensors)
            {
                var existingSensor = item.Sensors.FirstOrDefault(s => s.Name == sensor.Name && s.SensorType == sensor.SensorType.ToString());

                bool shouldIgnoreZeroCpuMetric = item.HardwareType == "Cpu" &&
                                                 (sensor.SensorType == SensorType.Clock ||
                                                  sensor.SensorType == SensorType.Power ||
                                                  sensor.SensorType == SensorType.Temperature) &&
                                                 (!sensor.Value.HasValue || sensor.Value <= 0);

                if (shouldIgnoreZeroCpuMetric)
                {
                    continue;
                }
                
                // QUALITY GATE:
                // 1. Never overwrite a valid NATIVE sensor (CPU) with a Motherboard placeholder (which are often 0).
                // 2. Never overwrite a previously valid >0 value with a 0 (common during Ryzen SMU poll lags).
                if (existingSensor != null && existingSensor.Value > 0)
                {
                    // If the new value is zero or invalid, stick to the last good value
                    if (sensor.Value == null || sensor.Value <= 0) continue;
                    
                    // If the new source is from Motherboard but the existing one is Native, ignore MB
                    if (fromMotherboard && !existingSensor.IsMotherboardSource) continue;
                }

                if (existingSensor == null)
                {
                    existingSensor = new HardwareSensor
                    {
                        Name = sensor.Name,
                        SensorType = sensor.SensorType.ToString(),
                    };
                    item.Sensors.Add(existingSensor);
                }

                existingSensor.IsMotherboardSource = fromMotherboard;
                existingSensor.Value = sensor.Value;
            }
        }

        public void Dispose()
        {
            _timer.Stop();
            _cpuFallbackTelemetry.Dispose();
            _computer.Close();
        }
    }
}
