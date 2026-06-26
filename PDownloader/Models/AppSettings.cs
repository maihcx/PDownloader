using System.Text.Json;

namespace PDownloader.Models;

/// <summary>
/// Application-level settings. Replaces LauncherSettings (OCR-focused).
/// </summary>
public class AppSettings
{
    public bool   StartWithWindows       { get; set; } = false;
    public bool   MinimizeToTray         { get; set; } = true;
    public bool   ShowRunnerAtBoot       { get; set; } = false;

    public string DefaultDownloadFolder  { get; set; } = string.Empty;
    public int    DefaultThreadCount     { get; set; } = 8;
    public int    MaxConcurrentDownloads { get; set; } = 3;

    public string AccentColor            { get; set; } = "#4FC3F7";
    public string Language               { get; set; } = "vi";
    public string Theme                  { get; set; } = "Dark";

    private const string StoreKey = "pd-app-settings-v1";

    public static AppSettings Load()
    {
        try
        {
            string? raw = UserDataStore.GetValue<string>(StoreKey);
            if (!string.IsNullOrWhiteSpace(raw))
                return JsonSerializer.Deserialize<AppSettings>(raw) ?? new();
        }
        catch { }
        return new();
    }

    public void Save()
    {
        try { UserDataStore.SetValue(StoreKey, JsonSerializer.Serialize(this)); }
        catch { }
    }
}
