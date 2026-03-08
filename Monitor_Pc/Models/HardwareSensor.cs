using CommunityToolkit.Mvvm.ComponentModel;

namespace Monitor_Pc.Models
{
    public partial class HardwareSensor : ObservableObject
    {
        [ObservableProperty]
        private string name = string.Empty;

        [ObservableProperty]
        private string sensorType = string.Empty;

        [ObservableProperty]
        private float? value;

        [ObservableProperty]
        private double valuePercentage;

        [ObservableProperty]
        private bool isMotherboardSource;

        [ObservableProperty]
        private string formattedValue = "--";

        public bool IsValid => Value.HasValue && !float.IsNaN(Value.Value) && !float.IsInfinity(Value.Value);

        partial void OnValueChanged(float? value)
        {
            if (value == null || float.IsNaN(value.Value) || float.IsInfinity(value.Value))
            {
                FormattedValue = "--";
                ValuePercentage = 0;
            }
            else
            {
                // Better rounding for small values to detect "ghost" data
                if (SensorType == "Temperature")
                {
                    FormattedValue = $"{value.Value:F1} °C";
                    ValuePercentage = Math.Clamp(value.Value / 100f * 100f, 0, 100);
                }
                else if (SensorType == "Load")
                {
                    FormattedValue = $"{value.Value:F1} %";
                    ValuePercentage = Math.Clamp(value.Value, 0, 100);
                }
                else if (SensorType == "Clock")
                {
                    FormattedValue = value.Value >= 1000 ? $"{value.Value / 1000f:F2} GHz" : $"{value.Value:F0} MHz";
                    ValuePercentage = Math.Clamp(value.Value / 5000f * 1000f, 0, 100);
                }
                else if (SensorType == "Voltage")
                {
                    FormattedValue = $"{value.Value:F3} V";
                    ValuePercentage = 0;
                }
                else if (SensorType == "Power")
                {
                    FormattedValue = $"{value.Value:F1} W";
                    ValuePercentage = 0;
                }
                else if (SensorType == "Fan")
                {
                    FormattedValue = $"{value.Value:F0} RPM";
                    ValuePercentage = 0;
                }
                else
                {
                    FormattedValue = value.Value.ToString("F2");
                    ValuePercentage = 0;
                }
            }
        }
    }
}
