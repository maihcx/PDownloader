namespace PDownloader.Core
{
    public static class DownloadManagerBootstrap
    {
        private static readonly object _saveLock = new();
        private static Timer? _saveDebounceTimer;
        private const int SaveDebounceMs = 1000;

        public static void InitDownloadManager()
        {
            DownloadManager.Instance.OnItemChanged += item =>
            {
                CFSIncomingHandler.BroadcastItemChanged(item);

                ScheduleSaveHistory();
            };

            RestoreHistoryOnStartup();
        }

        private static void ScheduleSaveHistory()
        {
            lock (_saveLock)
            {
                _saveDebounceTimer?.Dispose();
                _saveDebounceTimer = new Timer(_ => SaveHistoryNow(), null, SaveDebounceMs, Timeout.Infinite);
            }
        }

        private static void SaveHistoryNow()
        {
            try
            {
                Directory.CreateDirectory(StorageDataDir);
                var json = DownloadManager.Instance.SerializeHistory();
                File.WriteAllText(StorageDownloaderDataFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Bootstrap] Lưu lịch sử thất bại: {ex.Message}");
            }
        }

        private static void RestoreHistoryOnStartup()
        {
            try
            {
                if (!File.Exists(StorageDownloaderDataFile)) return;

                string json = File.ReadAllText(StorageDownloaderDataFile);
                var restored = DownloadManager.Instance.RestoreHistory(json);

                System.Diagnostics.Debug.WriteLine(
                    $"[Bootstrap] Đã khôi phục {restored.Count} item từ lịch sử.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Bootstrap] Khôi phục lịch sử thất bại: {ex.Message}");
            }
        }

        private static string StorageDataDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SM SOFT", "PDownloader");

        private static string StorageDownloaderDataFile =>
            Path.Combine(StorageDataDir, "downloads_history.json");
    }
}