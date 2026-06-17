using System.Globalization;
using System.Windows.Data;
using Core.Enums;

namespace UI.Converters
{
    public class PlatformToBrushKeyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is Platform platform && platform == Platform.Twitch ? "TwitchBrush" : "KickBrush";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }
}