namespace PDownloader.Core.Runtime
{
    public static class CFSIncomingHandler
    {
        private static readonly Dictionary<string, string> _last = new();

        private static readonly ConcurrentDictionary<string, YoutubePendingMeta> _youtubePending = new();

        public record YoutubePendingMeta(string FormatId);

        public static void Handle(string name, string value)
        {
            switch (name)
            {
                case "runner-start-download":
                    HandleStartDownload(value);
                    return;

                case "downloader-svc-getlist":
                    SendListToMain();
                    return;

                case "tray-event":
                case "state":
                case "main-event":
                case "core-svc-state":
                    _last.TryGetValue(name, out string? prev);
                    if (prev == value) return;
                    _last[name] = value;
                    CFSCommandHandler.Handle(name, value);
                    return;
            }
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

        private static void SendListToMain()
        {
            string json = DownloadManager.Instance.SerializeList();
            AppRuntime.cfsMain?.Send("muxt-get-downloader-list", json);
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

        public static string DownloadItem2Token(DownloadItem item)
        {
            var serialize = JsonSerializer.Serialize(new
            {
                url = item.Url,
                saveTo = item.SavePath  ?? string.Empty,
                fileName = item.FileName  ?? string.Empty
            });
            return Helpers.CreateMD5(serialize);
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
