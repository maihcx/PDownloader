using System.Text.Json;
using PDownloader.Core.Download;

namespace PDownloader.Core.Runtime
{
    /// <summary>
    /// Handles ALL incoming CFS messages to Core from: Main, Tray, Runner.
    ///
    /// CFS Commands (incoming):
    ///   "download"                  – browser extension/Main requests a new download → show Runner for confirm
    ///   "runner-start-download"     – Runner confirmed, start downloading in Core
    ///   "runner-pause"              – Runner requests pause    (value = id)
    ///   "runner-resume"             – Runner requests resume   (value = id)
    ///   "runner-cancel"             – Runner requests cancel   (value = id)
    ///   "runner-retry"              – Runner requests retry    (value = id)
    ///   "downloader-svc-getlist"    – Main requests full download list
    ///   "show-runner"               – bring Runner window to front
    ///   "tray-event"                – tray button clicked
    ///   "state"                     – app lifecycle (shutdown)
    ///   "main-event"                – Main → forward to Tray
    ///   "core-svc-state"            – core lifecycle
    /// </summary>
    public static class CFSIncomingHandler
    {
        private static readonly Dictionary<string, string> _last = new();

        private static readonly ConcurrentDictionary<string, YoutubePendingMeta> _youtubePending = new();

        public record YoutubePendingMeta(string FormatId);

        public static void Handle(string name, string value)
        {
            switch (name)
            {
                // ── Download flow ─────────────────────────────────────────────
                case "download":
                    // From extension/Main → forward to Runner for confirmation
                    // not work right now
                    HandleShowRunnerForDownload(value);
                    return;

                case "runner-start-download":
                    // Runner confirmed → actually start download in Core
                    HandleStartDownload(value);
                    return;

                case "runner-pause":
                    DownloadManager.Instance.Pause(value);
                    return;

                case "runner-resume":
                    DownloadManager.Instance.Resume(value);
                    return;

                case "runner-cancel":
                    DownloadManager.Instance.Cancel(value);
                    return;

                case "runner-retry":
                     DownloadManager.Instance.Retry(value);
                    return;

                // ── Main UI requests list ─────────────────────────────────────
                case "downloader-svc-getlist":
                    SendListToMain();
                    return;

                // ── Routing ───────────────────────────────────────────────────
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

        // ── Download confirmed by user in Runner ──────────────────────────────
        // value = JSON { url, saveTo, fileName, threads }
        private static void HandleStartDownload(string value)
        {
            try
            {
                var req = JsonSerializer.Deserialize<StartDownloadRequest>(value,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (req == null || string.IsNullOrWhiteSpace(req.Url)) return;

                _youtubePending.TryRemove(req.Id, out var ytMeta);

                var item = DownloadManager.Instance.Enqueue(
                    id: req.Id,
                    url: req.Url,
                    saveTo: req.SaveTo   ?? string.Empty,
                    fileName: req.FileName ?? string.Empty,
                    threads: req.Threads > 0 ? req.Threads : 8,
                    isYoutube: ytMeta != null,           // có trong cache → YouTube
                    formatId: ytMeta?.FormatId);        // lấy formatId từ cache

                BroadcastItemChanged(item);
            }
            catch { }
        }

        // ── Extension/Main sends a URL → forward to Runner to confirm ─────────
        // value = JSON { url, saveTo?, fileName? }
        private static void HandleShowRunnerForDownload(string value)
        {
            try
            {
                using var doc = JsonDocument.Parse(value);
                var root = doc.RootElement;
                if (!root.TryGetProperty("url", out var urlProp)) return;
                if (string.IsNullOrWhiteSpace(urlProp.GetString())) return;
            }
            catch { return; }

            AppRuntime.EnsureRunnerStarted(Helpers.CreateMD5(value)).Send("download", value);
        }

        // ── Send full list to Main ────────────────────────────────────────────
        private static void SendListToMain()
        {
            string json = DownloadManager.Instance.SerializeList();
            AppRuntime.cfsMain?.Send("muxt-get-downloader-list", json);
        }

        // ── Broadcast one item change to Runner + Main ────────────────────────
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
                saveTo = item.SavePath   ?? string.Empty,
                fileName = item.FileName ?? string.Empty
            });
            return Helpers.CreateMD5(serialize);
        }

        public static void RegisterYoutubePending(string id, string formatId) => _youtubePending[id] = new YoutubePendingMeta(formatId);

        private record StartDownloadRequest(
            string Id, string Url, string? SaveTo, string? FileName, int Threads);
    }
}
