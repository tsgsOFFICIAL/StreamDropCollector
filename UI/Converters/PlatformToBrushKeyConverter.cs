using System.Globalization;
using System.Windows.Data;
using Core.Enums;

namespace UI.Converters
{
    /// <summary>
    /// Maps a <see cref="Platform"/> value to the application resource key for its brand brush.
    /// </summary>
    public class PlatformToBrushKeyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is Platform platform && platform == Platform.Twitch ? "TwitchBrush" : "KickBrush";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }
}