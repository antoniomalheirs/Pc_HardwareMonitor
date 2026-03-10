using CommunityToolkit.Mvvm.ComponentModel;

namespace Monitor_Pc.Models
{
    public partial class HardwareSensor : ObservableObject
    {
        [ObservableProperty] private string name         = string.Empty;
        [ObservableProperty] private string sensorType   = string.Empty;
        [ObservableProperty] private string sensorId     = string.Empty;
        [ObservableProperty] private string unit         = string.Empty;
        [ObservableProperty] private float? value;
        [ObservableProperty] private double valuePercentage;
        [ObservableProperty] private bool   isMotherboardSource;
        [ObservableProperty] private string formattedValue = "--";
        [ObservableProperty] private HardwareSensor? linkedSensor;
        [ObservableProperty] private string combinedValue = "--";

        public bool IsValid =>
            Value.HasValue &&
            !float.IsNaN(Value.Value) &&
            !float.IsInfinity(Value.Value);

        partial void OnUnitChanged(string value) => RefreshFormattedValue();
        partial void OnValueChanged(float? value) => RefreshFormattedValue();

        private void RefreshFormattedValue()
        {
            var val = Value;
            if (val == null || float.IsNaN(val.Value) || float.IsInfinity(val.Value))
            {
                FormattedValue  = "--";
                ValuePercentage = 0;
            }
            else
            {
                float v = val.Value;
                bool unitHandled = false;

                // 1. Try to format based on explicit Unit string if available
                if (!string.IsNullOrEmpty(Unit))
                {
                    string u = Unit.Trim();
                    if (u.Equals("MHz", StringComparison.OrdinalIgnoreCase) || u.Equals("GHz", StringComparison.OrdinalIgnoreCase))
                    {
                        // Normalize to MHz for consistent formatting logic
                        float mhz = u.Equals("GHz", StringComparison.OrdinalIgnoreCase) ? v * 1000f : v;
                        FormattedValue = mhz >= 1000 ? $"{mhz / 1000f:F3} GHz" : $"{mhz:F1} MHz";
                        ValuePercentage = Math.Clamp(mhz / 5500f * 100f, 0, 100);
                        unitHandled = true;
                    }
                    else if (u.Equals("%"))
                    {
                        FormattedValue = $"{v:F1} %";
                        ValuePercentage = Math.Clamp(v, 0, 100);
                        unitHandled = true;
                    }
                    else if (u.Equals("°C") || u.Equals("C"))
                    {
                        FormattedValue = $"{v:F1} °C";
                        ValuePercentage = Math.Clamp(v, 0, 100);
                        unitHandled = true;
                    }
                    else if (u.Equals("V"))
                    {
                        FormattedValue = $"{v:F3} V";
                        unitHandled = true;
                    }
                    else if (u.Equals("W"))
                    {
                        FormattedValue = $"{v:F1} W";
                        unitHandled = true;
                    }
                }

                if (!unitHandled)
                {
                    // 2. Fallback to SensorType based formatting
                    switch (SensorType)
                    {
                        case "Temperature":
                            FormattedValue  = $"{v:F1} °C";
                            ValuePercentage = Math.Clamp(v, 0, 100);
                            break;

                        case "Load":
                            FormattedValue  = $"{v:F1} %";
                            ValuePercentage = Math.Clamp(v, 0, 100);
                            break;

                        case "Clock":
                            FormattedValue  = v >= 1000 ? $"{v / 1000f:F3} GHz" : $"{v:F1} MHz";
                            ValuePercentage = Math.Clamp(v / 5500f * 100f, 0, 100);
                            break;

                        case "Voltage":
                            FormattedValue  = $"{v:F3} V";
                            break;

                        case "Power":
                            FormattedValue  = $"{v:F1} W";
                            break;

                        case "Fan":
                            FormattedValue  = $"{v:F0} RPM";
                            break;

                        case "Current":
                            FormattedValue  = $"{v:F3} A";
                            break;

                        case "Throughput":
                        case "Data":
                            if (v >= 1_000_000f)      FormattedValue = $"{v / 1_000_000f:F2} GB/s";
                            else if (v >= 1_000f)     FormattedValue = $"{v / 1_000f:F2} MB/s";
                            else                      FormattedValue = $"{v:F1} KB/s";
                            break;

                        case "Factor":
                            FormattedValue  = v.ToString("F3");
                            break;

                        default:
                            FormattedValue  = v.ToString("F1") + (string.IsNullOrEmpty(Unit) ? "" : " " + Unit);
                            break;
                    }
                }

                UpdateCombinedValue();
            }
        }

        partial void OnLinkedSensorChanged(HardwareSensor? value)
        {
            UpdateCombinedValue();
        }

        private void UpdateCombinedValue()
        {
            CombinedValue = (LinkedSensor != null && LinkedSensor.IsValid)
                ? $"{FormattedValue} ({LinkedSensor.FormattedValue})"
                : FormattedValue;
        }
    }
}
