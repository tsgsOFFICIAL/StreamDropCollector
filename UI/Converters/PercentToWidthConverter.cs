using System.Globalization;
using System.Windows.Data;

namespace UI.Converters
{
    public class PercentToWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type t, object p, CultureInfo c)
        {
            if (values.Length == 2 && values[0] is double percent && values[1] is double width)
                return percent / 100.0 * width;
            return 0.0;
        }
        public object[] ConvertBack(object v, Type[] t, object p, CultureInfo c) => throw new NotSupportedException();
    }
}
