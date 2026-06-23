using System.Globalization;
using System.Windows.Data;
using System.Windows;

namespace UI.Converters
{
    /// <summary>
    /// Converts a boolean to <see cref="Visibility.Visible"/> or <see cref="Visibility.Collapsed"/>.
    /// </summary>
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && b ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}