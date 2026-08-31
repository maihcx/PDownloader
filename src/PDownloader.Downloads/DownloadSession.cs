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
/// One item's command queue, worker and cancellation source. Commands are async
/// continuations, not semaphore waiters; removing an item cannot dispose a gate
/// that another caller is still waiting on.
/// </summary>
internal sealed class DownloadSession : IAsyncDisposable
{
    private readonly object _sync = new();
    private Task _commandTail = Task.CompletedTask;
    private CancellationTokenSource? _workCancellation;
    private Task? _disposeTask;
    private long _generation;
    private bool _removed;

    public DownloadSession(DownloadItem item) => Item = item;
    public DownloadItem Item { get; }
    public Task Work { get; set; } = Task.CompletedTask;
    public long Generation => Volatile.Read(ref _generation);
    public bool IsRemoved => Volatile.Read(ref _removed);
    public void MarkRemoved() => Volatile.Write(ref _removed, true);

    public Task QueueCommand(Func<Task> action)
    {
        lock (_sync)
        {
            Task previous = _commandTail;
            _commandTail = Task.Run(async () =>
            {
                // A failed command must not poison later Pause/Cancel requests.
                try { await previous.ConfigureAwait(false); }
                catch (Exception) { }
                await action().ConfigureAwait(false);
            });
            return _commandTail;
        }
    }

    // Only the serialized command path may replace a generation's resources.
    public CancellationToken BeginWork(CancellationToken shutdown)
    {
        if (!Work.IsCompleted) throw new InvalidOperationException("Previous work is still running.");
        ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
        _workCancellation?.Dispose();
        _workCancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        Interlocked.Increment(ref _generation);
        return _workCancellation.Token;
    }

    public Task CancelWorkAsync() =>
        _workCancellation is null ? Work
            : Task.WhenAll(_workCancellation.CancelAsync(), Work);

    public async ValueTask DisposeAsync()
    {
        TaskCompletionSource? owner = null;
        Task completion;
        lock (_sync)
        {
            if (_disposeTask is null)
            {
                owner = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _disposeTask = owner.Task;
            }
            completion = _disposeTask;
        }
        if (owner is not null)
        {
            try
            {
                try { await CancelWorkAsync().ConfigureAwait(false); }
                finally
                {
                    _workCancellation?.Dispose();
                    _workCancellation = null;
                    GC.SuppressFinalize(this);
                }
                owner.TrySetResult();
            }
            catch (Exception ex) { owner.TrySetException(ex); }
        }
        await completion.ConfigureAwait(false);
    }
}
