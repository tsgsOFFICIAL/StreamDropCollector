using System.Windows.Media.Imaging;
using System.Globalization;
using System.Windows.Data;

namespace UI.Converters
{
    /// <summary>
    /// Loads a <see cref="BitmapImage"/> from an HTTP URL or pack URI string.
    /// </summary>
    public class StringToImageSourceConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string path || string.IsNullOrWhiteSpace(path))
                return null;

            try
            {
                Uri uri = ToImageUri(path);
                BitmapImage bitmap = new();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = uri;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        internal static Uri ToImageUri(string path)
        {
            if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return new Uri(path, UriKind.Absolute);

            if (path.StartsWith("pack://", StringComparison.OrdinalIgnoreCase))
                return new Uri(path, UriKind.Absolute);

            string normalized = path.StartsWith('/') ? path : "/" + path;
            return new Uri($"pack://application:,,,{normalized}", UriKind.Absolute);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }
}