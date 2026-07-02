using static PDownloader.Core.Runtime.CFSIncomingHandler;

namespace PDownloader.Core.Runtime
{
    public static class CFSCommandHandler
    {
        public record YoutubePendingMeta(string FormatId);

        private static readonly ConcurrentDictionary<string, YoutubePendingMeta> _youtubePending = new();

        public static void Handle(string name, string value)
        {
            switch (name)
            {
                case "main-event":
                    AppRuntime.cfsTray?.Send(name, value);
                    foreach (var (CFSkey, CFSvalue) in AppRuntime.DownloaderCFSRest)
                    {
                        CFSvalue.Send(name, value);
                    }
                    break;

                case "tray-event":
                case "state":
                    HandleMainEvent(name, value);
                    break;

                case "core-svc-state":
                    HandleCoreState(value);
                    break;

                case "downloader-svc-getlist":
                    SendListToMain();
                    return;

                case "runner-start-download":
                    HandleStartDownload(value);
                    return;

                case "runner-resume":
                    HandleShowRunnerForDownload(value);
                    DownloadManager.Instance.Resume(value);
                    return;

                case "runner-retry":
                    DownloadManager.Instance.Retry(value);
                    return;

                case "runner-cancel":
                    DownloadManager.Instance.Cancel(value);
                    return;

                case "runner-pause":
                    DownloadManager.Instance.Pause(value);
                    return;
            }
        }

        private static void HandleMainEvent(string name, string value)
        {
            if (!AppRuntime.cfsMain!.IsAppStarted())
                AppRuntime.cfsMain.StartApp();

            AppRuntime.cfsMain.Send(name, value);
        }

        private static void HandleCoreState(string value)
        {
            if (value == "shutdown")
            {
                if (AppRuntime.cfsMain!.IsAppStarted())
                {
                    AppRuntime.cfsMain.Send("state", value);
                }

                AppRuntime.bootstrap?.Shutdown();
            }
        }

        private static void HandleShowRunnerForDownload(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }
            DownloadItem? downloadItem = DownloadManager.Instance.Find(value);
            if (downloadItem != null)
            {
                AppRuntime.EnsureRunnerStarted(downloadItem.Id, new()
                {
                    id = downloadItem.Id,
                    fileName = downloadItem.FileName,
                    formatId = downloadItem.FormatId ?? string.Empty,
                    filesize = downloadItem.TotalBytes,
                    saveTo = downloadItem.SavePath,
                    url = downloadItem.Url,
                    downloadRunner = "runner",
                });
            }
        }

        private static void SendListToMain()
        {
            string json = DownloadManager.Instance.SerializeList();
            AppRuntime.cfsMain?.Send("muxt-get-downloader-list", json);
        }

        private static void HandleStartDownload(string value)
        {
            try
            {
                var req = JsonSerializer.Deserialize<StartDownloadRequest>(value,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (req == null || string.IsNullOrWhiteSpace(req.Url)) return;

                _youtubePending.TryRemove(req.Id, out var ytMeta);

                Dictionary<string, string>? customHeaders = null;
                if (req.Headers is { Count: > 0 })
                {
                    customHeaders = req.Headers
                        .Where(kv => !string.IsNullOrWhiteSpace(kv.Key)
                                  && !string.IsNullOrWhiteSpace(kv.Value))
                        .ToDictionary(kv => kv.Key, kv => kv.Value);

                    if (customHeaders.Count == 0) customHeaders = null;
                }

                var item = DownloadManager.Instance.Enqueue(
                    id: req.Id,
                    url: req.Url,
                    saveTo: req.SaveTo   ?? string.Empty,
                    fileName: req.FileName ?? string.Empty,
                    threads: req.Threads > 0 ? req.Threads : 8,
                    isYoutube: ytMeta != null,
                    formatId: ytMeta?.FormatId,
                    customHeaders: customHeaders);

                BroadcastItemChanged(item);
            }
            catch { }
        }

        public static void BroadcastItemChanged(DownloadItem item)
        {
            string json = DownloadManager.SerializeItem(item);
            AppRuntime.DownloaderCFSRest.TryGetValue(item.Id, out var cfsDowloaderUI);

            item.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(item.DownloadedFormatted))
                {
                    string json = DownloadManager.SerializeItem(item);
                    AppRuntime.cfsMain?.Send("muxt-download-progress", json);
                    AppRuntime.DownloaderCFSRest.TryGetValue(item.Id, out var cfsDowloaderUI);
                    cfsDowloaderUI?.Send("muxt-download-progress", json);
                }
            };
            AppRuntime.cfsMain?.Send("muxt-download-progress", json);
            cfsDowloaderUI?.Send("muxt-download-progress", json);
        }

        public static void RegisterYoutubePending(string id, string formatId)
            => _youtubePending[id] = new YoutubePendingMeta(formatId);

        private record StartDownloadRequest(
            string Id,
            string Url,
            string? SaveTo,
            string? FileName,
            int Threads,
            Dictionary<string, string>? Headers);
    }
}
