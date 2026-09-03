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

using System.Diagnostics;

namespace PDownloader.CFS;

public sealed partial class ConfluxService
{
    private readonly CancellationTokenSource _operationsCancellation = new();
    private int _activeOperations;
    private TaskCompletionSource? _operationsDrained;
    private Task? _disposeTask;

    private OperationLease? TryBeginOperation()
    {
        lock (_serviceSync)
        {
            if (_disposed)
            {
                return null;
            }

            var operation = new OperationLease(this, _operationsCancellation.Token);
            _activeOperations++;
            return operation;
        }
    }

    private void EndOperation()
    {
        lock (_serviceSync)
        {
            _activeOperations--;
            if (_activeOperations == 0)
            {
                _operationsDrained?.TrySetResult();
            }
        }
    }

    private sealed class OperationLease : IDisposable
    {
        // Borrowed owner; releasing a lease must not dispose the endpoint itself.
        private ConfluxService? _owner;
        public CancellationToken Token { get; }

        public OperationLease(ConfluxService owner, CancellationToken token)
        {
            _owner = owner;
            Token = token;
        }

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.EndOperation();
    }

    public async ValueTask DisposeAsync()
    {
        Task completionTask;
        Task drained = Task.CompletedTask;
        TaskCompletionSource? completion = null;
        lock (_serviceSync)
        {
            if (_disposeTask is null)
            {
                // Closing admission and registering the drain waiter are atomic
                // with TryBeginOperation, including operations waiting on a gate.
                _disposed = true;
                _ready = false;
                completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _disposeTask = completion.Task;
                if (_activeOperations != 0)
                {
                    _operationsDrained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    drained = _operationsDrained.Task;
                }
            }

            completionTask = _disposeTask;
        }

        if (completion is not null)
        {
            try
            {
                try
                {
                    // Cancellation callbacks run outside our locks. WhenAll waits
                    // for every participant, even if cancellation/stop faults.
                    await Task.WhenAll(_operationsCancellation.CancelAsync(),
                        StopServiceAsync(), drained).ConfigureAwait(false);
                }
                finally
                {
                    // No server worker, startup waiter, send or request can still
                    // use these resources. Dispose fields directly (also visible
                    // to ownership analysis) instead of leaving them for GC.
                    lock (_serviceSync)
                    {
                        _cts?.Dispose();
                        _cts = null;
                        _serviceTask = null;
                        _dispatchQueue = null;
                    }

                    _sendGate.Dispose();
                    _startGate.Dispose();
                    _operationsCancellation.Dispose();
                    lock (_processSync)
                    {
                        ReplaceCurrentProcess(null);
                    }

                    GC.SuppressFinalize(this);
                }

                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }

        // All repeated/concurrent Dispose calls observe the same completion.
        await completionTask.ConfigureAwait(false);
    }

    public void Dispose()
    {
        // Preserve the non-blocking WPF path. Async owners should await
        // DisposeAsync when they need all resource cleanup to have completed.
        _ = ObserveDisposeAsync();
    }

    private async Task ObserveDisposeAsync()
    {
        try { await DisposeAsync().ConfigureAwait(false); }
        catch (Exception ex) { Debug.WriteLine($"[CFS] Dispose failed: {ex}"); }
    }
}
