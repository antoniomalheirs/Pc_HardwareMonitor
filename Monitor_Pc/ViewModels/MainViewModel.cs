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
        private readonly HwInfoSharedMemoryTelemetry _hwInfoTelemetry;

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
            _hwInfoTelemetry = new HwInfoSharedMemoryTelemetry();

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

            ApplyHwInfoSensors(ref highestTemp, ref totalCpuLoad);

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
                    if (targetItem == null)
                    {
                        targetItem = new HardwareItem
                        {
                            Name = "CPU",
                            HardwareType = "Cpu"
                        };
                        HardwareItems.Add(targetItem);
                    }
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
                if (!sensor.Value.HasValue || sensor.Value <= 0)
                {
                    continue;
                }

                if (sensor.SensorType == SensorType.Temperature && IsPreferredCpuTemperature(sensor.Name))
                {
                    highestTemp = Math.Max(highestTemp, sensor.Value.Value);
                }

                if (sensor.SensorType == SensorType.Load && IsPreferredCpuLoad(sensor.Name))
                {
                    totalCpuLoad = Math.Max(totalCpuLoad, sensor.Value.Value);
                }
            }

            foreach (var subHardware in hardware.SubHardware)
            {
                ProcessHardwareRecursive(subHardware, ref highestTemp, ref totalCpuLoad);
            }
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

        private void ApplyCpuFallbackSensors(HardwareItem cpuItem)
        {
            UpsertCpuFallbackSensor(cpuItem, "CPU Package (Fallback)", "Temperature", _cpuFallbackTelemetry.TryReadTemperature);
            UpsertCpuFallbackSensor(cpuItem, "CPU Effective Clock (Fallback)", "Clock", _cpuFallbackTelemetry.TryReadClockMhz);
            UpsertCpuFallbackSensor(cpuItem, "CPU Package Power (Fallback)", "Power", _cpuFallbackTelemetry.TryReadPackagePower);
        }

        private static void UpsertCpuFallbackSensor(HardwareItem cpuItem, string sensorName, string sensorType, TryReadMetric readMetric)
        {
            if (!readMetric(out var value) || value <= 0)
            {
                return;
            }

            var sensor = cpuItem.Sensors.FirstOrDefault(s => s.Name == sensorName && s.SensorType == sensorType);
            if (sensor == null)
            {
                cpuItem.Sensors.Add(new HardwareSensor
                {
                    Name = sensorName,
                    SensorType = sensorType,
                    Value = value
                });

                return;
            }

            sensor.Value = value;
        }

        private void ApplyHwInfoSensors(ref float highestTemp, ref float totalCpuLoad)
        {
            if (!_hwInfoTelemetry.TryReadSnapshot(out var readings) || readings.Count == 0)
            {
                return;
            }

            var cpuItem = HardwareItems.FirstOrDefault(h => h.HardwareType == "Cpu");
            if (cpuItem != null)
            {
                ApplyHwInfoReading(cpuItem, readings, "CPU Package Temperature (HWiNFO)", "Temperature", out var cpuTemp,
                    "cpu", "package", "tctl", "tdie", "die", "ccd");
                ApplyHwInfoReading(cpuItem, readings, "CPU Total Usage (HWiNFO)", "Load", out var cpuLoad,
                    "cpu total", "total cpu usage", "total usage", "processor total");
                ApplyHwInfoReading(cpuItem, readings, "CPU Effective Clock (HWiNFO)", "Clock", out _,
                    "effective clock", "core clock", "average effective clock");
                ApplyHwInfoReading(cpuItem, readings, "CPU Package Power (HWiNFO)", "Power", out _,
                    "cpu package power", "package power", "cpu total power", "ppt");
                ApplyHwInfoReading(cpuItem, readings, "CPU Core Voltage (HWiNFO)", "Voltage", out _,
                    "vcore", "core voltage", "cpu core voltage", "svi2");

                if (cpuTemp > 0)
                {
                    highestTemp = Math.Max(highestTemp, cpuTemp);
                }

                if (cpuLoad > 0)
                {
                    totalCpuLoad = Math.Max(totalCpuLoad, cpuLoad);
                }

                cpuItem.RefreshGroups();
            }

            foreach (var gpuItem in HardwareItems.Where(h => h.HardwareType.StartsWith("Gpu", StringComparison.OrdinalIgnoreCase)))
            {
                var gpuName = gpuItem.Name;
                ApplyHwInfoReading(gpuItem, readings, "GPU Core Temperature (HWiNFO)", "Temperature", out _,
                    gpuName, "gpu", "core temperature", "gpu temperature");
                ApplyHwInfoReading(gpuItem, readings, "GPU Core Load (HWiNFO)", "Load", out _,
                    gpuName, "gpu", "core load", "gpu usage");
                ApplyHwInfoReading(gpuItem, readings, "GPU Core Clock (HWiNFO)", "Clock", out _,
                    gpuName, "gpu", "core clock", "gpu clock");
                ApplyHwInfoReading(gpuItem, readings, "GPU Power (HWiNFO)", "Power", out _,
                    gpuName, "gpu", "gpu power", "total board power");
                ApplyHwInfoReading(gpuItem, readings, "GPU Fan (HWiNFO)", "Fan", out _,
                    gpuName, "gpu", "fan", "gpu fan");

                gpuItem.RefreshGroups();
            }
        }

        private static void ApplyHwInfoReading(
            HardwareItem item,
            IReadOnlyList<HwInfoReading> readings,
            string targetName,
            string targetType,
            out float value,
            params string[] keywords)
        {
            value = 0;

            var reading = readings
                .Where(r => r.Value > 0)
                .Where(r => r.HasKeywords(keywords))
                .OrderByDescending(r => r.Value)
                .FirstOrDefault();

            if (reading == null)
            {
                return;
            }

            value = NormalizeValue(reading, targetType);
            if (value <= 0)
            {
                return;
            }

            var sensor = item.Sensors.FirstOrDefault(s => s.Name == targetName && s.SensorType == targetType);
            if (sensor == null)
            {
                item.Sensors.Add(new HardwareSensor
                {
                    Name = targetName,
                    SensorType = targetType,
                    Value = value
                });
                return;
            }

            sensor.Value = value;
        }

        private static float NormalizeValue(HwInfoReading reading, string targetType)
        {
            if (targetType == "Clock" && reading.Unit.Contains("GHz", StringComparison.OrdinalIgnoreCase))
            {
                return reading.Value * 1000f;
            }

            if (targetType == "Temperature" && reading.Unit.Contains("F", StringComparison.OrdinalIgnoreCase))
            {
                return (reading.Value - 32f) * (5f / 9f);
            }

            return reading.Value;
        }

        private static bool IsPreferredCpuTemperature(string sensorName)
        {
            return sensorName.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                   sensorName.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
                   sensorName.Contains("Tdie", StringComparison.OrdinalIgnoreCase) ||
                   sensorName.Contains("Average", StringComparison.OrdinalIgnoreCase) ||
                   sensorName.Contains("Core (Tctl/Tdie)", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPreferredCpuLoad(string sensorName)
        {
            return sensorName.Contains("Total", StringComparison.OrdinalIgnoreCase) ||
                   sensorName.Contains("CPU Total", StringComparison.OrdinalIgnoreCase) ||
                   sensorName.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                   sensorName.Contains("Core Max", StringComparison.OrdinalIgnoreCase);
        }

        public void Dispose()
        {
            _timer.Stop();
            _cpuFallbackTelemetry.Dispose();
            _hwInfoTelemetry.Dispose();
            _computer.Close();
        }

        private delegate bool TryReadMetric(out float value);
    }
}
