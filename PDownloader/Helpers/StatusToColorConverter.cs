using Color = System.Windows.Media.Color;

namespace PDownloader.Helpers
{
    internal class StatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value switch
            {
                "Completed" => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                "Error" => new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50)),
                "Paused" => new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00)),
                "Merging" => new SolidColorBrush(Color.FromRgb(0xAB, 0x47, 0xBC)),
                _ => new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7)) // blue
            };
            //Debug.WriteLine(value?.GetType().FullName);

            //return System.Drawing.Brushes.Red;
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }
}
