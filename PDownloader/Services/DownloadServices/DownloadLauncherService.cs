namespace PDownloader.Services.DownloadServices
{
    public class DownloadLauncherService
    {
        public bool IsDaemonRunning => ConfluxManager.cfsPDownloaderCore != null;

        public void RequestDownload(string url, string saveTo = "", string fileName = "")
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            var payload = JsonSerializer.Serialize(new
            {
                url,
                saveTo = saveTo.Trim(),
                fileName = fileName.Trim()
            });

            ConfluxManager.cfsPDownloaderCore?.Send("download", payload);
        }

        public void ShowRunner()
        {
            ConfluxManager.cfsPDownloaderCore?.Send("show-runner", string.Empty);
        }

        public void ApplySettings(AppSettings settings)
        {
            ConfluxManager.cfsPDownloaderCore?.Send("app-settings", JsonSerializer.Serialize(settings));
        }
    }
}
