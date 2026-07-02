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

        public void RefreshConfigs()
        {
            ConfluxManager.cfsPDownloaderCore?.Send("core-event", "refresh-downloader-configs");
        }
    }
}
