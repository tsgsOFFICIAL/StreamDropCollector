using System.Globalization;
using System.Windows.Data;

namespace UI.Converters
{
    public class ResourceKeyToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string key && System.Windows.Application.Current.TryFindResource(key) is System.Windows.Media.Brush brush)
                return brush;

            return System.Windows.Application.Current.TryFindResource("TextPrimaryBrush") as System.Windows.Media.Brush
                ?? System.Windows.Media.Brushes.White;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }
}