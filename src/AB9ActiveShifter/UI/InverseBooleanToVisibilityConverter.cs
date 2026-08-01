using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AB9ActiveShifter.UI
{
    /// <summary>
    /// The inverse of the standard BooleanToVisibilityConverter, for the geometry percent
    /// display toggle: the raw-count slider of each pair shows exactly when the percent one
    /// does not, and WPF's built-in converter has no parameter to flip its own sense.
    /// </summary>
    public sealed class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isChecked = value is bool && (bool)value;
            return isChecked ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
