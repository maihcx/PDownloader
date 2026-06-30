using PDownloader.Runner.Resources;

namespace PDownloader.Runner.Utils;

/// <summary>
/// Handles incoming CFS messages from PDownloader.Core → Runner.
///
/// Commands received:
///   "download"              – Core forwards a new URL, show confirmation dialog
///   "muxt-download-progress"– Core broadcasts progress of a running download (display only)
///   "state"                 – lifecycle (shutdown)
/// </summary>
public static class RunnerCommandHandler
{
    public static void Handle(string name, string value)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            switch (name)
            {
                case "cancel":
                    break;

                //case "download":
                //    HandleDownloadRequest(value);
                //    break;

                case "main-event":
                    switch (value)
                    {
                        case "state":
                            if (value == "shutdown")
                                System.Windows.Application.Current?.Shutdown();
                            break;

                        case "OnLanguageChanged":
                            UserDataStore.Reload();
                            TranslationSource.Instance.CurrentCulture = LanguageBase.GetSetupLanguage();
                            break;

                        case "OnRadiusChanged":
                            UserDataStore.Reload();
                            Application.Current.Resources["ControlCornerRadius"] = new CornerRadius(UserDataStore.GetValue<int>("ObjectCornerRadius"));
                            break;

                        case "OnMaterialChanged":
                            UserDataStore.Reload();
                            AppRuntime.ThemeManagerService?.SetBackdropType(Enum.Parse<WindowBackdropType>(AppRuntime.ThemeManagerService.GetMaterialCBBSelected()?.Value ?? "Mica"));
                            AppRuntime.ThemeManagerService?.SetApplicationTheme(Enum.Parse<ThemeConfigs.IThemeType>(AppRuntime.ThemeManagerService.GetThemeCBBSelected()?.Value ?? "Auto"));
                            break;

                        case "OnThemeChanged":
                            UserDataStore.Reload();
                            AppRuntime.ThemeManagerService?.SetApplicationTheme(Enum.Parse<ThemeConfigs.IThemeType>(AppRuntime.ThemeManagerService.GetThemeCBBSelected()?.Value ?? "Auto"));
                            break;

                        case "OnAppExit":
                            Application.Current.Shutdown();
                            break;
                    }
                    break;
            }
        });
    }

    // value = JSON { url, saveTo?, fileName? }
    //private static void HandleDownloadRequest(string value)
    //{
    //    try
    //    {
    //        using var doc = JsonDocument.Parse(value);
    //        var root = doc.RootElement;

    //        string url      = root.TryGetProperty("url",      out var u) ? u.GetString() ?? "" : "";
    //        string saveTo   = root.TryGetProperty("saveTo",   out var s) ? s.GetString() ?? "" : "";
    //        string fileName = root.TryGetProperty("fileName", out var f) ? f.GetString() ?? "" : "";

    //        if (string.IsNullOrWhiteSpace(url)) return;

    //        var win = AppRuntime.MainWindow;
    //        if (win == null) return;

    //        win.ShowForDownload(url, saveTo, fileName);
    //    }
    //    catch { }
    //}
}
