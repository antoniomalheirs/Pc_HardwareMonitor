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

        // ── Min / Max / Avg tracking ─────────────────────────────────────────
        [ObservableProperty] private float? minValue;
        [ObservableProperty] private float? maxValue;
        [ObservableProperty] private float? avgValue;
        [ObservableProperty] private string formattedMinMax = "";
        private double _avgAccum;
        private long   _avgCount;

        // ── Alert severity ───────────────────────────────────────────────────
        [ObservableProperty] private AlertSeverity alertLevel = AlertSeverity.Normal;
        [ObservableProperty] private string alertColor = "Transparent";

        // ── Progress bar ─────────────────────────────────────────────────────
        [ObservableProperty] private double progressPercent;
        [ObservableProperty] private string progressColor = "#4CAF50";  // green default
        [ObservableProperty] private bool   showProgressBar;

        public bool IsValid =>
            Value.HasValue &&
            !float.IsNaN(Value.Value) &&
            !float.IsInfinity(Value.Value);

        partial void OnUnitChanged(string value) => RefreshFormattedValue();
        partial void OnValueChanged(float? value) => RefreshFormattedValue();

        /// <summary>Reset min/max/avg tracking.</summary>
        public void ResetStatistics()
        {
            MinValue = null;
            MaxValue = null;
            AvgValue = null;
            _avgAccum = 0;
            _avgCount = 0;
            FormattedMinMax = "";
        }

        private void UpdateMinMaxAvg(float v)
        {
            if (!MinValue.HasValue || v < MinValue.Value) MinValue = v;
            if (!MaxValue.HasValue || v > MaxValue.Value) MaxValue = v;

            _avgAccum += v;
            _avgCount++;
            AvgValue = (float)(_avgAccum / _avgCount);

            FormattedMinMax = $"▼ {FormatCompact(MinValue.Value)}  ▲ {FormatCompact(MaxValue.Value)}";
        }

        /// <summary>Set min/max/avg from external source (e.g., HWiNFO).</summary>
        public void SetExternalMinMaxAvg(double min, double max, double avg)
        {
            if (min != 0 || max != 0)
            {
                MinValue = (float)min;
                MaxValue = (float)max;
                AvgValue = (float)avg;
                FormattedMinMax = $"▼ {FormatCompact((float)min)}  ▲ {FormatCompact((float)max)}";
            }
        }

        private string FormatCompact(float v)
        {
            string u = Unit?.Trim() ?? "";
            if (u.Equals("MHz", StringComparison.OrdinalIgnoreCase) || SensorType == "Clock")
                return v >= 1000 ? $"{v / 1000f:F1}G" : $"{v:F0}M";
            if (u.Equals("°C") || u.Equals("C") || SensorType == "Temperature")
                return $"{v:F0}°";
            if (u.Equals("%") || SensorType == "Load")
                return $"{v:F0}%";
            if (u.Equals("V") || SensorType == "Voltage")
                return $"{v:F3}V";
            if (u.Equals("W") || SensorType == "Power")
                return $"{v:F0}W";
            if (SensorType == "Fan")
                return $"{v:F0}";
            return v.ToString("F1");
        }

        private void UpdateAlertAndProgress(float v)
        {
            // ── Alert evaluation ─────────────────────────────────────────
            AlertLevel = AlertThresholds.Evaluate(SensorType, Name, v);
            AlertColor = AlertLevel switch
            {
                AlertSeverity.Critical => "#FF5252",
                AlertSeverity.Warning  => "#FFD740",
                _                      => "Transparent"
            };

            // ── Progress bar ─────────────────────────────────────────────
            bool shouldShow = SensorType is "Temperature" or "Load" or "Fan";
            ShowProgressBar = shouldShow;
            if (!shouldShow) return;

            double pct;
            if (SensorType == "Temperature")
            {
                pct = Math.Clamp((v - 25) / 80.0 * 100.0, 0, 100);
            }
            else if (SensorType == "Load")
            {
                pct = Math.Clamp(v, 0, 100);
            }
            else // Fan
            {
                pct = Math.Clamp(v / 3000.0 * 100.0, 0, 100);
            }
            ProgressPercent = pct;

            // Gradient: green (0%) → yellow (60%) → red (100%)
            ProgressColor = pct switch
            {
                >= 85 => "#FF5252",
                >= 70 => "#FF9800",
                >= 55 => "#FFC107",
                >= 40 => "#FFEB3B",
                _     => "#4CAF50"
            };
        }

        private void RefreshFormattedValue()
        {
            var val = Value;
            if (val == null || float.IsNaN(val.Value) || float.IsInfinity(val.Value))
            {
                FormattedValue  = "--";
                ValuePercentage = 0;
                ShowProgressBar = false;
                AlertLevel      = AlertSeverity.Normal;
                AlertColor      = "Transparent";
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

                UpdateMinMaxAvg(v);
                UpdateAlertAndProgress(v);
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
