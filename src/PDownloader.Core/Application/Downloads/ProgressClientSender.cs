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

using System.Threading.Channels;

namespace PDownloader.Core.Application.Downloads;

/// <summary>
/// One serial sender per UI process. The bounded channel carries only wake-ups;
/// the keyed mailbox retains at most one pending snapshot per download, plus the
/// single in-flight snapshot. Memory scales with pending IDs, not progress events.
/// </summary>
internal sealed class ProgressClientSender : IAsyncDisposable
{
    private static readonly TimeSpan PublishInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);
    private readonly ConfluxService _channel; // Borrowed; Core/session owns the channel.
    private readonly int _processId;
    private readonly object _sync = new();
    private readonly Dictionary<string, DownloadItemDto> _pending = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();
    private readonly Channel<byte> _wake = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.DropWrite,
        AllowSynchronousContinuations = false
    });
    private readonly CancellationTokenSource _lifetime;
    private readonly Task _worker;
    private Task? _disposeTask;
    private bool _stopping;

    public ProgressClientSender(ConfluxService channel, int processId,
        CancellationToken sessionLifetime = default)
    {
        _channel = channel;
        _processId = processId;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(sessionLifetime);
        _worker = Task.Run(RunAsync);
        _channel.TargetExited += OnTargetExited;
        // Also check an exit that raced event subscription, even for an empty UI.
        _wake.Writer.TryWrite(0);
    }

    public Task Completion => _worker;

    public bool Matches(ConfluxService channel, int processId)
    {
        lock (_sync)
            return !_stopping && !_worker.IsCompleted
                && ReferenceEquals(_channel, channel) && _processId == processId;
    }

    public void Publish(DownloadItemDto snapshot)
    {
        lock (_sync)
        {
            if (_stopping) return;
            if (!_pending.ContainsKey(snapshot.Id)) _order.Enqueue(snapshot.Id);
            _pending[snapshot.Id] = snapshot;
            _wake.Writer.TryWrite(0);
        }
    }

    private void OnTargetExited(int processId)
    {
        if (processId == _processId) _ = DisposeAsync();
    }

    private bool IsCurrentProcess()
    {
        try { return _channel.GetProcess().Id == _processId; }
        catch (InvalidOperationException) { return false; }
    }

    private async Task RunAsync()
    {
        CancellationToken token = _lifetime.Token;
        try
        {
            while (await _wake.Reader.WaitToReadAsync(token).ConfigureAwait(false))
            {
                while (_wake.Reader.TryRead(out _)) { }
                // Fixed cadence rather than debounce: continuous progress cannot
                // postpone a send indefinitely, and hot IDs do not starve others.
                await Task.Delay(PublishInterval, token).ConfigureAwait(false);
                if (!IsCurrentProcess()) return;

                int batchSize;
                lock (_sync) batchSize = _order.Count;
                for (int index = 0; index < batchSize; index++)
                {
                    DownloadItemDto snapshot;
                    lock (_sync)
                    {
                        if (_stopping || _order.Count == 0) break;
                        string id = _order.Dequeue();
                        snapshot = _pending[id];
                        _pending.Remove(id);
                    }

                    token.ThrowIfCancellationRequested();
                    if (!IsCurrentProcess()) return;
                    bool sent;
                    try
                    {
                        sent = await _channel.SendAsync(DownloadProtocol.Progress, snapshot,
                            SendTimeout, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Progress] Send failed: {ex.Message}");
                        sent = false;
                    }

                    if (!sent)
                    {
                        if (!IsCurrentProcess()) return;
                        lock (_sync)
                        {
                            // Never restore an old in-flight DTO over a newer one.
                            // Retain terminal states too, even with no future callback.
                            if (!_stopping && !_pending.ContainsKey(snapshot.Id))
                            {
                                _pending[snapshot.Id] = snapshot;
                                _order.Enqueue(snapshot.Id);
                            }
                            if (!_stopping) _wake.Writer.TryWrite(0);
                        }
                        await Task.Delay(RetryDelay, token).ConfigureAwait(false);
                        break;
                    }
                }

                lock (_sync)
                    if (!_stopping && _order.Count > 0) _wake.Writer.TryWrite(0);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Session ended or Core is shutting down; UI progress is not persistence.
        }
        finally
        {
            lock (_sync)
            {
                _stopping = true;
                _pending.Clear();
                _order.Clear();
                _wake.Writer.TryComplete();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        TaskCompletionSource? completion = null;
        Task completionTask;
        lock (_sync)
        {
            if (_disposeTask is null)
            {
                _stopping = true;
                _wake.Writer.TryComplete();
                _channel.TargetExited -= OnTargetExited;
                completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _disposeTask = completion.Task;
            }
            completionTask = _disposeTask;
        }

        if (completion is not null)
        {
            try
            {
                // Cancel outside the mailbox lock; await callbacks AND the worker
                // before releasing the CTS. The borrowed IPC channel stays alive.
                await Task.WhenAll(_lifetime.CancelAsync(), _worker).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Progress] Sender cleanup: {ex.Message}");
            }
            finally
            {
                _lifetime.Dispose();
                GC.SuppressFinalize(this);
                completion.TrySetResult();
            }
        }
        await completionTask.ConfigureAwait(false);
    }
}
