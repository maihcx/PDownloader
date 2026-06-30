namespace PDownloader.Runner.Helpers
{
    public class EnumToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType,
                              object parameter, CultureInfo culture)
            => value?.ToString() == parameter?.ToString()
                ? Visibility.Visible
                : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType,
                                  object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
