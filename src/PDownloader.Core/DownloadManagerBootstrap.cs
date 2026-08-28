// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
//
// Copyright (C) Song Mai Software.

namespace PDownloader.Core;

/// <summary>
/// Wires process-owned download persistence and publication to DownloadManager.
/// </summary>
public sealed class DownloadManagerBootstrap : IDisposable
{
    private const int SaveDebounceMs = 1000;

    private readonly DownloadProgressPublisher _progressPublisher;
    private readonly object _saveLock = new();
    private Timer? _saveDebounceTimer;
    private bool _initialized;
    private bool _disposed;

    public DownloadManagerBootstrap(DownloadProgressPublisher progressPublisher)
    {
        _progressPublisher = progressPublisher;
    }

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized)
        {
            return;
        }

        DownloadManager.Instance.OnItemChanged += OnItemChanged;
        RestoreHistoryOnStartup();
        _initialized = true;
    }

    private void OnItemChanged(DownloadItem item)
    {
        _progressPublisher.Publish(item);
        ScheduleSaveHistory();
    }

    private void ScheduleSaveHistory()
    {
        lock (_saveLock)
        {
            _saveDebounceTimer?.Dispose();
            _saveDebounceTimer = new Timer(
                _ => SaveHistoryNow(),
                null,
                SaveDebounceMs,
                Timeout.Infinite);
        }
    }

    private static void SaveHistoryNow()
    {
        try
        {
            Directory.CreateDirectory(StorageDataDir);
            string json = DownloadManager.Instance.SerializeHistory();
            File.WriteAllText(StorageDownloaderDataFile, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Bootstrap] Lưu lịch sử thất bại: {ex.Message}");
        }
    }

    private static void RestoreHistoryOnStartup()
    {
        try
        {
            if (!File.Exists(StorageDownloaderDataFile))
            {
                return;
            }

            string json = File.ReadAllText(StorageDownloaderDataFile);
            List<DownloadItem> restored = DownloadManager.Instance.RestoreHistory(json);

            Debug.WriteLine(
                $"[Bootstrap] Đã khôi phục {restored.Count} item từ lịch sử.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Bootstrap] Khôi phục lịch sử thất bại: {ex.Message}");
        }
    }

    private static string StorageDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SM SOFT",
        "PDownloader");

    private static string StorageDownloaderDataFile =>
        Path.Combine(StorageDataDir, "downloads_history.json");

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_initialized)
        {
            DownloadManager.Instance.OnItemChanged -= OnItemChanged;
        }

        lock (_saveLock)
        {
            _saveDebounceTimer?.Dispose();
            _saveDebounceTimer = null;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
