namespace PDownloader.Runner.Helpers
{
    [ValueConversion(typeof(bool), typeof(Visibility))]
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter is string p &&
                p.Equals("invert", StringComparison.OrdinalIgnoreCase))
            {
                return value is true
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }

            if (value is DownloadStatus status && parameter is string s)
            {
                return status.ToString().Equals(s, StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            return value is true
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
