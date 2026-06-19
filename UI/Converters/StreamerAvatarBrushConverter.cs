using System.Globalization;
using System.Windows.Media;
using System.Windows.Data;

namespace UI.Converters
{
    /// <summary>
    /// Selects a deterministic avatar background brush from a color index.
    /// </summary>
    public sealed class StreamerAvatarBrushConverter : IValueConverter
    {
        private static readonly System.Windows.Media.Brush[] Palette =
        [
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(92, 107, 192)),
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(38, 166, 154)),
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 83, 80)),
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(171, 71, 188)),
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 167, 38)),
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(66, 165, 245)),
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(141, 110, 99)),
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(102, 187, 106)),
        ];

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            int index = value is int i ? i : 0;
            if (index < 0 || index >= Palette.Length)
                index = 0;

            return Palette[index];
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}