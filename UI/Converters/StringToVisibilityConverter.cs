using System.Globalization;
using System.Windows.Data;
using System.Windows;

namespace UI.Converters
{
    /// <summary>
    /// Shows bound content when a string is non-empty; otherwise collapses the target.
    /// </summary>
    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}