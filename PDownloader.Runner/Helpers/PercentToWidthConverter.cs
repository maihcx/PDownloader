namespace PDownloader.Runner.Helpers
{
    public class PercentToWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType,
                              object parameter, CultureInfo culture)
        {
            if (values[0] is double pct && values[1] is double w)
                return Math.Max(0, Math.Min(w, w * pct / 100.0));
            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes,
                                    object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
