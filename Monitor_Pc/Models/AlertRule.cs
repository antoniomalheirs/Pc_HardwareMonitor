using CommunityToolkit.Mvvm.ComponentModel;

namespace Monitor_Pc.Models
{
    public enum AlertSeverity { Normal, Warning, Critical }

    public partial class ActiveAlert : ObservableObject
    {
        [ObservableProperty] private string sensorName = "";
        [ObservableProperty] private string message    = "";
        [ObservableProperty] private AlertSeverity severity = AlertSeverity.Warning;
        [ObservableProperty] private string icon       = "⚠";
        [ObservableProperty] private string accentColor = "#FFE082";
        [ObservableProperty] private DateTime timestamp = DateTime.Now;

        public string SeverityColor => Severity switch
        {
            AlertSeverity.Critical => "#FF5252",
            AlertSeverity.Warning  => "#FFE082",
            _                      => "#FFFFFF"
        };
    }

    /// <summary>
    /// Static alert threshold definitions for hardware monitoring.
    /// </summary>
    public static class AlertThresholds
    {
        // ── Temperature thresholds ──────────────────────────────────────────
        public const float TempWarning  = 80f;   // °C
        public const float TempCritical = 92f;   // °C

        // ── Voltage thresholds (ATX ±5%) ────────────────────────────────────
        // +12V rail: 11.4V – 12.6V
        public const float V12_Nominal = 12.0f;
        public const float V12_Low     = 11.4f;
        public const float V12_High    = 12.6f;

        // +5V rail: 4.75V – 5.25V
        public const float V5_Nominal  = 5.0f;
        public const float V5_Low      = 4.75f;
        public const float V5_High     = 5.25f;

        // +3.3V rail: 3.135V – 3.465V
        public const float V33_Nominal = 3.3f;
        public const float V33_Low     = 3.135f;
        public const float V33_High    = 3.465f;

        // ── Fan thresholds ───────────────────────────────────────────────────
        public const float FanMinRpm   = 200f;   // Below this = possibly dead fan

        /// <summary>Evaluate a sensor and return its alert severity.</summary>
        public static AlertSeverity Evaluate(string sensorType, string sensorName, float value)
        {
            switch (sensorType)
            {
                case "Temperature":
                    if (value >= TempCritical) return AlertSeverity.Critical;
                    if (value >= TempWarning)  return AlertSeverity.Warning;
                    break;

                case "Voltage":
                    if (IsRail(sensorName, "+12V", "12V"))
                    {
                        if (value < V12_Low || value > V12_High)   return AlertSeverity.Critical;
                        if (value < V12_Low * 1.01f || value > V12_High * 0.99f) return AlertSeverity.Warning;
                    }
                    else if (IsRail(sensorName, "+5V", "5V"))
                    {
                        if (value < V5_Low || value > V5_High)     return AlertSeverity.Critical;
                        if (value < V5_Low * 1.01f || value > V5_High * 0.99f) return AlertSeverity.Warning;
                    }
                    else if (IsRail(sensorName, "3VCC", "3VSB", "3V", "VBAT"))
                    {
                        if (value < V33_Low || value > V33_High)   return AlertSeverity.Critical;
                        if (value < V33_Low * 1.01f || value > V33_High * 0.99f) return AlertSeverity.Warning;
                    }
                    break;

                case "Fan":
                    if (value > 0 && value < FanMinRpm) return AlertSeverity.Warning;
                    break;
            }
            return AlertSeverity.Normal;
        }

        private static bool IsRail(string name, params string[] keywords)
        {
            foreach (var kw in keywords)
                if (name.Contains(kw, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
