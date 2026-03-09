using CommunityToolkit.Mvvm.ComponentModel;

namespace Monitor_Pc.Models
{
    public partial class HardwareSensor : ObservableObject
    {
        [ObservableProperty] private string name         = string.Empty;
        [ObservableProperty] private string sensorType   = string.Empty;
        [ObservableProperty] private string sensorId     = string.Empty;
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

        partial void OnValueChanged(float? value)
        {
            if (value == null || float.IsNaN(value.Value) || float.IsInfinity(value.Value))
            {
                FormattedValue  = "--";
                ValuePercentage = 0;
            }
            else
            {
                switch (SensorType)
                {
                    case "Temperature":
                        FormattedValue  = $"{value.Value:F1} °C";
                        ValuePercentage = Math.Clamp(value.Value / 100f * 100f, 0, 100);
                        break;

                    case "Load":
                        FormattedValue  = $"{value.Value:F1} %";
                        ValuePercentage = Math.Clamp(value.Value, 0, 100);
                        break;

                    case "Clock":
                        FormattedValue  = value.Value >= 1000
                            ? $"{value.Value / 1000f:F3} GHz"
                            : $"{value.Value:F1} MHz";
                        ValuePercentage = Math.Clamp(value.Value / 5500f * 100f, 0, 100);
                        break;

                    case "Voltage":
                        FormattedValue  = $"{value.Value:F3} V";
                        ValuePercentage = 0;
                        break;

                    case "Power":
                        FormattedValue  = $"{value.Value:F1} W";
                        ValuePercentage = 0;
                        break;

                    case "Fan":
                        FormattedValue  = $"{value.Value:F0} RPM";
                        ValuePercentage = 0;
                        break;

                    case "Current":                         // ← was missing proper handling
                        FormattedValue  = $"{value.Value:F3} A";
                        ValuePercentage = 0;
                        break;

                    case "Throughput":
                    case "Data":
                        // HWiNFO reports network / disk throughput in KB/s
                        if (value.Value >= 1_000_000f)
                            FormattedValue = $"{value.Value / 1_000_000f:F2} GB/s";
                        else if (value.Value >= 1_000f)
                            FormattedValue = $"{value.Value / 1_000f:F2} MB/s";
                        else
                            FormattedValue = $"{value.Value:F1} KB/s";
                        ValuePercentage = 0;
                        break;

                    case "Factor":
                        FormattedValue  = value.Value.ToString("F3");
                        ValuePercentage = 0;
                        break;

                    default:
                        FormattedValue  = value.Value.ToString("F1");
                        ValuePercentage = 0;
                        break;
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
