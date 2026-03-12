using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Monitor_Pc.Converters
{
    /// <summary>
    /// Converts a percentage (0-100) to a proportional width relative to the parent container.
    /// Used for progress bars where we need the inner bar to scale proportionally.
    /// Returns the percentage as a fraction of the ActualWidth (passed as parameter or defaults to 280).
    /// </summary>
    public class PercentToWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double percent)
            {
                double maxWidth = 280; // default max width matches the category column
                if (parameter is string s && double.TryParse(s, out double pw))
                    maxWidth = pw;

                return Math.Max(0, Math.Min(maxWidth, percent / 100.0 * maxWidth));
            }
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converts a string to Visibility: empty/null → Collapsed, non-empty → Visible.
    /// </summary>
    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s && !string.IsNullOrEmpty(s))
                return Visibility.Visible;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
