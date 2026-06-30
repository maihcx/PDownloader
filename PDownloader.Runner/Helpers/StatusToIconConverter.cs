namespace PDownloader.Runner.Helpers
{

    [ValueConversion(typeof(DownloadStatus), typeof(string))]
    public class StatusToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (DownloadStatus)value switch
            {
                DownloadStatus.Queued => "⏳",
                DownloadStatus.Connecting => "🔗",
                DownloadStatus.Downloading => "⬇",
                DownloadStatus.Paused => "⏸",
                DownloadStatus.Merging => "🔧",
                DownloadStatus.Completed => "✅",
                DownloadStatus.Error => "❌",
                _ => "?"
            };
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }
}
