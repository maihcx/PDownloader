namespace PDownloader.Runner.Helpers
{
    [ValueConversion(typeof(bool), typeof(Visibility))]
    public class InverseBoolToVisibility : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Also support Count == 0 comparison for empty state
            if (value is int count && parameter is string s && int.TryParse(s, out int zero))
                return count == zero ? Visibility.Visible : Visibility.Collapsed;

            return value is true ? Visibility.Collapsed : Visibility.Visible;
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }
}
