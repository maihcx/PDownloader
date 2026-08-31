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

/// <summary>Owns the history subscription and debounce timer, not DownloadManager.</summary>
public sealed class DownloadManagerBootstrap : IAsyncDisposable
{
    private const int SaveDebounceMs = 1000;
    private readonly DownloadProgressPublisher _progressPublisher;
    private readonly DownloadManager _downloads;
    private readonly object _saveLock = new();
    private Timer? _saveDebounceTimer;
    private bool _initialized;
    private bool _historyLoaded;
    private Task? _disposeTask;

    public DownloadManagerBootstrap(DownloadProgressPublisher progressPublisher, DownloadManager downloads)
    {
        _progressPublisher = progressPublisher;
        _downloads = downloads;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        lock (_saveLock)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
            if (_initialized) return;
            _initialized = true;
            _downloads.OnItemChanged += OnItemChanged;
            _saveDebounceTimer = new Timer(_ => SaveHistoryNow(), null, Timeout.Infinite, Timeout.Infinite);
        }
        try
        {
            if (File.Exists(StorageDownloaderDataFile))
            {
                string json = await File.ReadAllTextAsync(StorageDownloaderDataFile, cancellationToken)
                    .ConfigureAwait(false);
                List<DownloadItem> restored = await _downloads.RestoreHistoryAsync(json, cancellationToken)
                    .ConfigureAwait(false);
                Debug.WriteLine($"[Bootstrap] Restored {restored.Count} history items.");
            }
            lock (_saveLock) _historyLoaded = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // Do not overwrite an unreadable history with an empty shutdown snapshot.
            Debug.WriteLine($"[Bootstrap] History could not be restored; saving disabled: {ex.Message}");
        }
    }

    private void OnItemChanged(DownloadItem item)
    {
        _progressPublisher.Publish(item);
        lock (_saveLock)
        {
            if (_disposeTask is null)
                _saveDebounceTimer?.Change(SaveDebounceMs, Timeout.Infinite);
        }
    }

    private void SaveHistoryNow()
    {
        lock (_saveLock)
        {
            if (!_historyLoaded) return;
            try
            {
                Directory.CreateDirectory(StorageDataDir);
                File.WriteAllText(StorageDownloaderDataFile, _downloads.SerializeHistory());
            }
            catch (Exception ex) { Debug.WriteLine($"[Bootstrap] History save failed: {ex.Message}"); }
        }
    }

    private static string StorageDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SM SOFT", "PDownloader");
    private static string StorageDownloaderDataFile => Path.Combine(StorageDataDir, "downloads_history.json");

    public async ValueTask DisposeAsync()
    {
        TaskCompletionSource? owner = null;
        Task completion;
        Task timerStopped = Task.CompletedTask;
        lock (_saveLock)
        {
            if (_disposeTask is null)
            {
                owner = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _disposeTask = owner.Task;
                if (_initialized) _downloads.OnItemChanged -= OnItemChanged;
                timerStopped = _saveDebounceTimer?.DisposeAsync().AsTask() ?? Task.CompletedTask;
                _saveDebounceTimer = null;
            }
            completion = _disposeTask;
        }
        if (owner is not null)
        {
            try
            {
                // Do not hold _saveLock while waiting for timer callbacks.
                await timerStopped.ConfigureAwait(false);
                // Bootstrap calls this only after downloads/hash work has stopped.
                SaveHistoryNow();
                GC.SuppressFinalize(this);
                owner.TrySetResult();
            }
            catch (Exception ex) { owner.TrySetException(ex); }
        }
        await completion.ConfigureAwait(false);
    }
}
