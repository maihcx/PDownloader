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

using PDownloader.Infrastructure.Persistence;

namespace PDownloader.Core;

/// <summary>Owns history persistence; progress callbacks never perform disk I/O.</summary>
public sealed class DownloadManagerBootstrap : IAsyncDisposable
{
    private readonly DownloadProgressPublisher _progressPublisher;
    private readonly DownloadManager _downloads;
    private readonly object _lifecycleLock = new();
    private readonly CancellationTokenSource _stopSaving = new();
    private Task? _initializeTask;
    private Task _saveLoopTask = Task.CompletedTask;
    private Task? _disposeTask;
    private bool _historyLoaded;
    private long _changeVersion;
    private long _savedVersion;

    public DownloadManagerBootstrap(DownloadProgressPublisher progressPublisher, DownloadManager downloads)
    {
        _progressPublisher = progressPublisher;
        _downloads = downloads;
    }

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
            if (_initializeTask is null)
            {
                _downloads.OnItemChanged += OnItemChanged;
                _initializeTask = Task.Run(() => InitializeCoreAsync(cancellationToken));
            }

            return _initializeTask;
        }
    }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? json = AtomicFile.ReadAllText(StorageDownloaderDataFile,
                value => { DownloadManager.DeserializeHistory(value); }, out bool recovered);
            cancellationToken.ThrowIfCancellationRequested();
            if (json is not null)
            {
                List<DownloadItem> restored = await _downloads.RestoreHistoryAsync(json, cancellationToken)
                    .ConfigureAwait(false);
                Debug.WriteLine($"[Bootstrap] Restored {restored.Count} history items; backup={recovered}.");
            }

            _historyLoaded = true;
            lock (_lifecycleLock)
            {
                if (_disposeTask is null)
                {
                    _saveLoopTask = Task.Run(SaveLoopAsync);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // Keep both files untouched if neither can be safely restored.
            Debug.WriteLine($"[Bootstrap] History could not be restored; saving disabled: {ex.Message}");
        }
    }

    private void OnItemChanged(DownloadItem item)
    {
        Interlocked.Increment(ref _changeVersion);
        _progressPublisher.Publish(item);
    }

    private async Task SaveLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(_stopSaving.Token).ConfigureAwait(false))
            {
                if (Interlocked.Read(ref _changeVersion) != _savedVersion)
                {
                    SaveHistoryNow();
                }
            }
        }
        catch (OperationCanceledException) when (_stopSaving.IsCancellationRequested) { }
    }

    private void SaveHistoryNow()
    {
        if (!_historyLoaded)
        {
            return;
        }

        long version = Interlocked.Read(ref _changeVersion);
        try
        {
            string json = _downloads.SerializeHistory();
            // Never replace a good backup with an invalid in-memory snapshot.
            DownloadManager.DeserializeHistory(json);
            AtomicFile.WriteAllText(StorageDownloaderDataFile, json);
            _savedVersion = version;
        }
        catch (Exception ex)
        {
            // Keep the dirty version so the next tick retries even without progress.
            Debug.WriteLine($"[Bootstrap] History save failed: {ex.Message}");
        }
    }

    private static string StorageDownloaderDataFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SM SOFT", "PDownloader", "downloads_history.json");

    public async ValueTask DisposeAsync()
    {
        TaskCompletionSource? owner = null;
        Task completion;
        Task initialization;
        lock (_lifecycleLock)
        {
            if (_disposeTask is null)
            {
                owner = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _disposeTask = owner.Task;
                _downloads.OnItemChanged -= OnItemChanged;
            }

            completion = _disposeTask;
            initialization = _initializeTask ?? Task.CompletedTask;
        }

        if (owner is not null)
        {
            try
            {
                await _stopSaving.CancelAsync().ConfigureAwait(false);
                try { await initialization.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                // The writer has exclusive ownership until it has fully stopped.
                await _saveLoopTask.ConfigureAwait(false);
                // Bootstrap calls this only after download/hash workers have stopped.
                await Task.Run(SaveHistoryNow).ConfigureAwait(false);
                owner.TrySetResult();
            }
            catch (Exception ex) { owner.TrySetException(ex); }
            finally
            {
                _stopSaving.Dispose();
                GC.SuppressFinalize(this);
            }
        }

        await completion.ConfigureAwait(false);
    }
}
