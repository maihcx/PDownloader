namespace PDownloader.Runner.Helpers
{
    public class ProgressToWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values[0] is double pct && values[1] is double total)
                return Math.Max(0, pct / 100.0 * total);
            return 0.0;
        }
        public object[] ConvertBack(object v, Type[] t, object p, CultureInfo c) => Array.Empty<object>();
    }
}
