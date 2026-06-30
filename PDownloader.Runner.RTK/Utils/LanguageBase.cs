using System.Globalization;

namespace PDownloader.Runner.Utils;

public static class LanguageBase
{
    public static CultureInfo GetSetupLanguage()
    {
        try
        {
            string lang = UserDataStore.GetValue<string>("Language") ?? "vi";
            return new CultureInfo(lang);
        }
        catch
        {
            return new CultureInfo("vi");
        }
    }
}
