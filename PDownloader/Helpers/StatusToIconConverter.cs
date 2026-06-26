namespace PDownloader.Helpers
{
    internal class StatusToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            //Debug.WriteLine(value);
            return value switch
            {
                "Queued" => "⏳",
                "Connecting" => "🔗",
                "Downloading" => "⬇",
                "Paused" => "⏸",
                "Merging" => "🔧",
                "Completed" => "✅",
                "Error" => "❌",
                _ => "?"
            };
            //return "⏳";
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }
}
