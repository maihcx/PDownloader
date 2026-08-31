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

/// <summary>
/// Owns download sessions. Each session serializes its commands independently;
/// transfer/hash work is tracked separately from command completion.
/// </summary>
public sealed partial class DownloadManager : IAsyncDisposable
{
    private readonly IDownloadRuntime _runtime;
    private readonly DownloadPathService _pathService;
    private readonly YtDlpService _ytDlpService;
    private readonly FfmpegMuxer _ffmpegMuxer;
    private readonly object _sync = new();
    private readonly Dictionary<string, DownloadSession> _sessions = new(StringComparer.Ordinal);
    private readonly HashSet<Task> _commands = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _hashSemaphore = new(1, 1);
    private Task? _disposeTask;

    public event Action<DownloadItem>? OnItemChanged;

    public DownloadManager(IDownloadRuntime runtime, YtDlpService ytDlpService)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _ytDlpService = ytDlpService ?? throw new ArgumentNullException(nameof(ytDlpService));
        _pathService = new DownloadPathService(_runtime);
        _ffmpegMuxer = new FfmpegMuxer();
    }

    /// <summary>Returns after the download is registered and scheduled, not after transfer completion.</summary>
    public async Task<DownloadItem> EnqueueAsync(
        string id, string url, string saveTo = "", string fileName = "",
        int threads = 8, bool isYoutube = false, string? formatId = null,
        Dictionary<string, string>? customHeaders = null,
        FileMergeMode mergeMode = FileMergeMode.Balanced,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        cancellationToken.ThrowIfCancellationRequested();
        DownloadSession session;
        Task start;
        lock (_sync)
        {
            ThrowIfStopping();
            // Duplicate requests for a registered ID never create another worker.
            if (_sessions.TryGetValue(id, out session!))
            {
                return session.Item;
            }

            session = new DownloadSession(new DownloadItem
            {
                Id = id, Url = url, SavePath = saveTo, FileName = fileName,
                Threads = threads, IsYoutube = isYoutube, FormatId = formatId,
                CustomHeaders = customHeaders is null ? null
                    : new Dictionary<string, string>(customHeaders, StringComparer.OrdinalIgnoreCase),
                MergeMode = mergeMode, Status = DownloadStatus.Queued
            });
            _sessions.Add(id, session);
            start = TrackCommand(session.QueueCommand(() =>
            {
                if (!_shutdown.IsCancellationRequested)
                {
                    StartWork(session, hashOnly: false);
                }
                else
                {
                    session.Item.Status = DownloadStatus.Paused;
                }

                return Task.CompletedTask;
            }));
        }

        await start.ConfigureAwait(false);
        return session.Item;
    }

    public Task PauseAsync(string id, CancellationToken cancellationToken = default) =>
        Submit(id, (session, _) => PauseCoreAsync(session), cancellationToken);

    public Task ResumeAsync(string id, bool isShowRunner = true,
        CancellationToken cancellationToken = default) =>
        Submit(id, (session, generation) =>
            RestartCoreAsync(session, generation, retry: false, isShowRunner), cancellationToken);

    public Task RetryAsync(string id, CancellationToken cancellationToken = default) =>
        Submit(id, (session, generation) =>
            RestartCoreAsync(session, generation, retry: true, showRunner: false), cancellationToken);

    public Task CancelAsync(string id, CancellationToken cancellationToken = default) =>
        Submit(id, (session, _) => CancelCoreAsync(session), cancellationToken);

    public Task PauseAllAsync(CancellationToken cancellationToken = default) =>
        SubmitAll(session => !session.IsRemoved && (session.Item.CanPause
                || session.Item.Status is DownloadStatus.Queued or DownloadStatus.Connecting or DownloadStatus.Retrying),
            (session, _) => PauseCoreAsync(session), cancellationToken);

    public Task ResumeAllAsync(CancellationToken cancellationToken = default) =>
        SubmitAll(session => session.Item.Status == DownloadStatus.Paused,
            (session, generation) => RestartCoreAsync(session, generation, retry: false, showRunner: true),
            cancellationToken);

    public Task RetryAllAsync(CancellationToken cancellationToken = default) =>
        SubmitAll(session => session.Item.Status == DownloadStatus.Error,
            (session, generation) => RestartCoreAsync(session, generation, retry: true, showRunner: false),
            cancellationToken);

    public Task ClearAllAsync(DownloadClearScope scope, CancellationToken cancellationToken = default) =>
        SubmitAll(session => scope == DownloadClearScope.All
                || (scope == DownloadClearScope.Completed && session.Item.Status == DownloadStatus.Completed),
            (session, _) => CancelCoreAsync(session), cancellationToken);

    private Task Submit(string id, Func<DownloadSession, long, Task> action, CancellationToken token)
    {
        // The caller may cancel before admission. An admitted command keeps its
        // place and finishes cleanup even if that caller/IPC connection goes away.
        token.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ThrowIfStopping();
            if (!_sessions.TryGetValue(id, out DownloadSession? session))
            {
                return Task.CompletedTask;
            }

            long generation = session.Generation;
            return TrackCommand(session.QueueCommand(() => action(session, generation)));
        }
    }

    private Task SubmitAll(Func<DownloadSession, bool> predicate,
        Func<DownloadSession, long, Task> action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ThrowIfStopping();
            Task[] tasks = _sessions.Values.Where(predicate).Select(session =>
            {
                long generation = session.Generation;
                return TrackCommand(session.QueueCommand(() => action(session, generation)));
            }).ToArray();
            return Task.WhenAll(tasks);
        }
    }

    // Called under _sync, before shutdown can snapshot admitted commands.
    private Task TrackCommand(Task task)
    {
        _commands.Add(task);
        _ = ObserveCommandAsync(task);
        return task;
    }

    private async Task ObserveCommandAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.WriteLine($"[DownloadManager] Command failed: {ex.Message}"); }
        finally { lock (_sync)
            {
                _commands.Remove(task);
            }
        }
    }

    private void ThrowIfStopping() =>
        ObjectDisposedException.ThrowIf(_disposeTask is not null, this);

    public List<DownloadItem> GetAll()
    {
        lock (_sync)
        {
            return _sessions.Values.Where(session => !session.IsRemoved)
                .Select(session => session.Item).ToList();
        }
    }

    public DownloadItem? Find(string id)
    {
        lock (_sync)
        {
            return _sessions.TryGetValue(id, out DownloadSession? session) && !session.IsRemoved
                ? session.Item : null;
        }
    }

    public string SerializeHistory() =>
        JsonSerializer.Serialize(GetAll().Select(DownloadItemSnapshot.From),
            new JsonSerializerOptions { WriteIndented = true });

    public static List<DownloadItemSnapshot> DeserializeHistory(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new();
        }
        // Let the persistence owner distinguish corrupt input from an empty list.
        return JsonSerializer.Deserialize<List<DownloadItemSnapshot>>(json) ?? new();
    }

    public async Task<List<DownloadItem>> RestoreHistoryAsync(string json,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<DownloadItemSnapshot> snapshots = DeserializeHistory(json);
        var restored = new List<DownloadItem>();
        var commands = new List<Task>();
        lock (_sync)
        {
            ThrowIfStopping();
            foreach (DownloadItemSnapshot snapshot in snapshots)
            {
                DownloadItem item = snapshot.ToDownloadItem();
                if (string.IsNullOrWhiteSpace(item.Id) || _sessions.ContainsKey(item.Id))
                {
                    continue;
                }

                bool resumeRetry = item.Status == DownloadStatus.Retrying;
                if (item.Status is DownloadStatus.Queued or DownloadStatus.Connecting
                    or DownloadStatus.Downloading or DownloadStatus.Merging or DownloadStatus.Retrying)
                {
                    item.Status = DownloadStatus.Paused;
                    item.SpeedBps = 0;
                }

                var session = new DownloadSession(item);
                _sessions.Add(item.Id, session);
                restored.Add(item);
                commands.Add(TrackCommand(session.QueueCommand(() =>
                {
                    if (_shutdown.IsCancellationRequested)
                    {
                        return Task.CompletedTask;
                    }

                    if ((item.Status is DownloadStatus.Paused or DownloadStatus.Error)
                        && TryGetPendingMergeProgress(item, out double progress))
                    {
                        item.MergeProgress = progress;
                        item.IsMergeProgressActive = true;
                    }

                    Notify(item);
                    if (item.Status == DownloadStatus.Completed)
                    {
                        StartWork(session, hashOnly: true);
                    }
                    else if (resumeRetry)
                    {
                        StartWork(session, hashOnly: false);
                    }

                    return Task.CompletedTask;
                })));
            }
        }

        await Task.WhenAll(commands).ConfigureAwait(false);
        return restored;
    }

    public List<DownloadItemDto> GetContractList() =>
        GetAll().Select(DownloadItemContractMapper.From).ToList();

    public static DownloadItemDto ToContract(DownloadItem item) => DownloadItemContractMapper.From(item);

    private bool HasPendingMerge(DownloadItem item) =>
        MergeRecoveryStore.HasPendingInTree(_pathService.GetTempDirectory(item));

    private bool TryGetPendingMergeProgress(DownloadItem item, out double progress) =>
        MergeRecoveryStore.TryGetPendingProgressInTree(_pathService.GetTempDirectory(item), out progress);

    private void Notify(DownloadItem item)
    {
        // Observers must not fault a transfer or prevent resource cleanup.
        Delegate[] handlers = OnItemChanged?.GetInvocationList() ?? Array.Empty<Delegate>();
        foreach (Action<DownloadItem> handler in handlers)
        {
            try { handler(item); }
            catch (Exception ex) { Debug.WriteLine($"[DownloadManager] Observer failed: {ex.Message}"); }
        }
    }
}
