namespace PDownloader.Helpers
{
    internal class BoolToGreenRedBrush : IValueConverter
    {
        public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        {
            return (bool)value
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94))
                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
        }

        public object ConvertBack(object value, Type t, object? p, CultureInfo c)
        {
            throw new NotImplementedException();
        }
    }
}
