namespace PDownloader.Tray.Utils
{
    public static class LocalizationHelper
    {
        public static string GetLang(string value)
        {
            return Resources.Locales.String.ResourceManager.GetString(value, TranslationSource.Instance.CurrentCulture) ?? "";
        }
    }
}
