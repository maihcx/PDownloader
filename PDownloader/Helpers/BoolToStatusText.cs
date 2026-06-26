using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PDownloader.Helpers
{
    internal class BoolToStatusText : IValueConverter
    {
        public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        {
            return (bool)value ? $"● {Resources.Locales.String.ResourceManager.GetString("page_config_svc_active_title", TranslationSource.Instance.CurrentCulture)}" : $"● {Resources.Locales.String.ResourceManager.GetString("page_config_svc_inactive_title", TranslationSource.Instance.CurrentCulture)}";
        }

        public object ConvertBack(object value, Type t, object? p, CultureInfo c)
        {
            throw new NotImplementedException();
        }
    }
}
