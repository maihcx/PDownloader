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

namespace PDownloader.Downloads;

public sealed partial class DownloadManager
{
    private async Task PauseCoreAsync(DownloadSession session)
    {
        if (session.IsRemoved || _shutdown.IsCancellationRequested)
        {
            return;
        }

        DownloadItem item = session.Item;
        bool canPause = item.CanPause || item.Status is DownloadStatus.Queued
            or DownloadStatus.Connecting or DownloadStatus.Retrying;
        if (!canPause || session.Work.IsCompleted)
        {
            return;
        }

        await session.CancelWorkAsync().ConfigureAwait(false);
        // RunTransferAsync normalizes the state after the engine has unwound.
    }

    private async Task RestartCoreAsync(DownloadSession session, long expectedGeneration,
        bool retry, bool showRunner)
    {
        if (session.IsRemoved || _shutdown.IsCancellationRequested
            || session.Generation != expectedGeneration)
        {
            return;
        }

        DownloadItem item = session.Item;
        if (retry ? item.Status != DownloadStatus.Error : !item.CanResume)
        {
            return;
        }

        // Error/Paused may be published just before the old worker completes.
        await session.Work.ConfigureAwait(false);
        if (session.IsRemoved || _shutdown.IsCancellationRequested
            || session.Generation != expectedGeneration)
        {
            return;
        }

        if (retry ? item.Status != DownloadStatus.Error : !item.CanResume)
        {
            return;
        }

        if (retry)
        {
            bool pendingMerge = HasPendingMerge(item);
            item.MergeProgress = 0;
            item.IsMergeProgressActive = pendingMerge;
            if (!pendingMerge)
            {
                item.DownloadedBytes = 0;
            }

            item.Md5Hash = item.Sha1Hash = item.Sha256Hash = string.Empty;
        }

        StartWork(session, hashOnly: false);
        if (showRunner)
        {
            _runtime.ShowRunner(item.Id, new RunnerDownloadTask
            {
                Id = item.Id, FileName = item.FileName, FormatId = item.FormatId ?? string.Empty,
                FileSize = item.TotalBytes, SaveTo = item.SavePath, Url = item.Url,
                IsRunner = true, Threads = item.Threads,
                Headers = item.CustomHeaders is null ? null
                    : new Dictionary<string, string>(item.CustomHeaders, StringComparer.OrdinalIgnoreCase)
            });
        }
    }

    private async Task CancelCoreAsync(DownloadSession session)
    {
        if (session.IsRemoved)
        {
            return;
        }

        session.MarkRemoved(); // Suppress progress from the old worker immediately.
        try
        {
            // Includes hashing: do not delete files while any owned task uses them.
            try { await session.CancelWorkAsync().ConfigureAwait(false); }
            catch (Exception ex)
            {
                // CancelWorkAsync joins the worker even if a cancellation callback
                // faults. Cleanup must still retire the item after that join.
                Debug.WriteLine($"[DownloadManager] Cancellation cleanup: {ex.Message}");
            }

            session.Item.Status = DownloadStatus.Cancelled;
            session.Item.SpeedBps = 0;
            Notify(session.Item);
            _pathService.DeleteTempFiles(session.Item);
        }
        finally
        {
            try { await session.DisposeAsync().ConfigureAwait(false); }
            finally
            {
                lock (_sync)
                {
                    if (_sessions.TryGetValue(session.Item.Id, out DownloadSession? current)
                        && ReferenceEquals(current, session))
                    {
                        _sessions.Remove(session.Item.Id);
                    }
                }
            }
        }
    }

    private void StartWork(DownloadSession session, bool hashOnly)
    {
        if (session.IsRemoved || _shutdown.IsCancellationRequested || !session.Work.IsCompleted)
        {
            return;
        }

        CancellationToken token = session.BeginWork(_shutdown.Token);
        long generation = session.Generation;
        if (!hashOnly)
        {
            session.Item.Status = DownloadStatus.Connecting;
            session.Item.ErrorMessage = string.Empty;
            session.Item.SpeedBps = 0;
            Notify(session.Item);
        }
        // Exactly one worker per generation, recorded before another command runs.
        // There is deliberately no semaphore limiting the number of files.
        session.Work = Task.Run(() => hashOnly
            ? CalculateFileHashesAsync(session, token)
            : RunTransferAsync(session, generation, token));
    }

    private async Task RunTransferAsync(DownloadSession session, long generation, CancellationToken token)
    {
        DownloadItem item = session.Item;
        var progress = new InlineProgress<DownloadProgress>(_ =>
        {
            if (!token.IsCancellationRequested && !session.IsRemoved
                && session.Generation == generation)
            {
                Notify(item);
            }
        });
        try
        {
            const int maxAutoRetries = 5;
            for (int attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                item.Status = DownloadStatus.Connecting;
                var engine = new DownloadEngine(item, progress, token, _pathService, _ytDlpService, _ffmpegMuxer);
                try
                {
                    await engine.RunAsync().ConfigureAwait(false);
                    if (item.Status != DownloadStatus.Completed)
                    {
                        token.ThrowIfCancellationRequested();
                    }

                    break;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    token.ThrowIfCancellationRequested();
                    if (attempt >= maxAutoRetries)
                    {
                        item.Status = DownloadStatus.Error;
                        item.ErrorMessage = ex.Message;
                        break;
                    }

                    item.Status = DownloadStatus.Retrying;
                    item.ErrorMessage = $"An error occurred! Retrying ({attempt + 1}/{maxAutoRetries})... Please wait...";
                    Notify(item);
                    await Task.Delay(2000, token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A late Pause must not downgrade a file already committed successfully.
            if (item.Status != DownloadStatus.Completed)
            {
                item.Status = DownloadStatus.Paused;
            }
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested)
            {
                if (item.Status != DownloadStatus.Completed)
                {
                    item.Status = DownloadStatus.Paused;
                }
            }
            else
            {
                item.Status = DownloadStatus.Error;
                item.ErrorMessage = ex.Message;
            }
        }
        finally
        {
            item.SpeedBps = 0;
            if (!session.IsRemoved)
            {
                Notify(item);
            }
        }

        if (!session.IsRemoved && !token.IsCancellationRequested && item.Status == DownloadStatus.Completed)
        {
            await CalculateFileHashesAsync(session, token).ConfigureAwait(false);
        }
    }

    private async Task CalculateFileHashesAsync(DownloadSession session, CancellationToken token)
    {
        DownloadItem item = session.Item;
        string filePath = item.SavePath;
        if (session.IsRemoved || item.Status != DownloadStatus.Completed || item.HasFileHashes
            || string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return;
        }

        bool entered = false;
        try
        {
            await _hashSemaphore.WaitAsync(token).ConfigureAwait(false);
            entered = true;
            FileHashResult hashes = await FileHashCalculator.ComputeAsync(filePath, token).ConfigureAwait(false);
            if (token.IsCancellationRequested || session.IsRemoved || item.Status != DownloadStatus.Completed
                || !string.Equals(item.SavePath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            item.Md5Hash = hashes.Md5;
            item.Sha1Hash = hashes.Sha1;
            item.Sha256Hash = hashes.Sha256;
            Notify(item);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DownloadManager] Cannot calculate hash for '{filePath}': {ex.Message}");
        }
        finally
        {
            if (entered)
            {
                _hashSemaphore.Release();
            }
        }
    }

    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;
        public InlineProgress(Action<T> report) => _report = report;
        public void Report(T value) => _report(value);
    }
}
