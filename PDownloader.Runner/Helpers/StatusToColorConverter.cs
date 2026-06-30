namespace PDownloader.Runner.Helpers
{
    [ValueConversion(typeof(DownloadStatus), typeof(System.Windows.Media.Brush))]
    public class StatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (DownloadStatus)value switch
            {
                DownloadStatus.Completed => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                DownloadStatus.Error => new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50)),
                DownloadStatus.Paused => new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00)),
                DownloadStatus.Merging => new SolidColorBrush(Color.FromRgb(0xAB, 0x47, 0xBC)),
                _ => new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7)) // blue
            };
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }
}
