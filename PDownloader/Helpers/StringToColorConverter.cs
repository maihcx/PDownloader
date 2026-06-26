using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PDownloader.Helpers
{
    internal class StringToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        {
            return (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                .ConvertFromString(value?.ToString() ?? "#4FC3F7");
        }

        public object ConvertBack(object value, Type t, object? p, CultureInfo c)
        {
            throw new NotImplementedException();
        }
    }
}
