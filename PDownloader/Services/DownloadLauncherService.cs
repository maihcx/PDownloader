using System.Text.Json;

namespace PDownloader.Services;

/// <summary>
/// Replaces OverlayLauncherService.
/// Sends download commands from the main UI to Core → Runner via CFS.
/// </summary>
public class DownloadLauncherService
{
    public bool IsDaemonRunning => ConfluxManager.cfsPDownloaderCore != null;

    /// <summary>Queue a new download in Runner.</summary>
    public void RequestDownload(string url, string saveTo = "", string fileName = "")
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        var payload = JsonSerializer.Serialize(new
        {
            url,
            saveTo   = saveTo.Trim(),
            fileName = fileName.Trim()
        });

        ConfluxManager.cfsPDownloaderCore?.Send("download", payload);
    }

    /// <summary>Bring Runner window to front (no new download).</summary>
    public void ShowRunner()
    {
        ConfluxManager.cfsPDownloaderCore?.Send("show-runner", string.Empty);
    }

    /// <summary>Push updated app settings to Core.</summary>
    public void ApplySettings(AppSettings settings)
    {
        ConfluxManager.cfsPDownloaderCore?.Send("app-settings", JsonSerializer.Serialize(settings));
    }
}
