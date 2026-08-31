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
using System.IO.Pipes;
using System.Threading.Channels;

namespace PDownloader.CFS;

public sealed partial class ConfluxService
{
    private Channel<Func<CancellationToken, Task>>? _dispatchQueue;

    public Task StartServiceAsync()
    {
        lock (_serviceSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_stopTask is { IsCompleted: false })
            {
                throw new InvalidOperationException("The endpoint is still stopping.");
            }

            if (_cts is not null)
            {
                return Task.CompletedTask;
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(ReceivePipeName);
            if (MaxConcurrentConnections is < 2 or > 64)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxConcurrentConnections));
            }

            // Create every listener before reporting startup success. Startup failures
            // propagate to the host instead of leaving a silently broken background loop.
            var listeners = new List<NamedPipeServerStream>();
            try
            {
                for (int i = 0; i < MaxConcurrentConnections; i++)
                {
                    listeners.Add(CreateListener(firstInstance: i == 0));
                }
            }
            catch
            {
                foreach (NamedPipeServerStream listener in listeners)
                {
                    listener.Dispose();
                }

                throw;
            }

            _cts = new CancellationTokenSource();
            _stopTask = null;
            _dispatchQueue = Channel.CreateBounded<Func<CancellationToken, Task>>(
                new BoundedChannelOptions(128)
                {
                    SingleReader = true,
                    FullMode = BoundedChannelFullMode.Wait,
                    AllowSynchronousContinuations = false
                });
            CancellationToken token = _cts.Token;
            _serviceTask = Task.WhenAll(listeners.Select(pipe => RunWorkerAsync(pipe, token))
                .Append(DispatchLoopAsync(_dispatchQueue.Reader, token)));
            return Task.CompletedTask;
        }
    }

    private ValueTask QueueDispatchAsync(Func<CancellationToken, Task> work, CancellationToken token) =>
        (_dispatchQueue ?? throw new InvalidOperationException("IPC service is not running."))
            .Writer.WriteAsync(work, token);

    private static async Task DispatchLoopAsync(
        ChannelReader<Func<CancellationToken, Task>> reader, CancellationToken token)
    {
        try
        {
            await foreach (Func<CancellationToken, Task>? work in reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                try { await work(token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception ex) { Debug.WriteLine($"[CFS] Dispatch failed: {ex}"); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private NamedPipeServerStream CreateListener(bool firstInstance = false) => NamedPipeServerStreamAcl.Create(
        ReceivePipeName, PipeDirection.InOut, MaxConcurrentConnections,
        PipeTransmissionMode.Byte, PipeOptions.Asynchronous
            | (firstInstance ? PipeOptions.FirstPipeInstance : PipeOptions.None),
        inBufferSize: 0, outBufferSize: 0, pipeSecurity: CreateRestrictedPipeSecurity());

    private async Task RunWorkerAsync(NamedPipeServerStream initial, CancellationToken token)
    {
        NamedPipeServerStream? pipe = initial;
        while (!token.IsCancellationRequested)
        {
            try
            {
                pipe ??= CreateListener();
                await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                await HandleServerConnectionAsync(pipe, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (OperationCanceledException) { /* Incomplete/slow frame deadline. */ }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CFS] Endpoint '{ReceivePipeName}': {ex.Message}");
                try { await Task.Delay(100, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
            finally
            {
                pipe?.Dispose();
                pipe = null;
            }
        }

        pipe?.Dispose();
    }

    public Task StopServiceAsync()
    {
        lock (_serviceSync)
        {
            if (_stopTask is not null)
            {
                return _stopTask;
            }

            if (_cts is null)
            {
                return Task.CompletedTask;
            }

            _ready = false;
            _dispatchQueue?.Writer.TryComplete();
            // Cancel outside the lock and always join workers, including when a
            // cancellation callback faults. Never dispose a CTS while it is in use.
            CancellationTokenSource cts = _cts;
            Task workers = _serviceTask ?? Task.CompletedTask;
            _stopTask = Task.Run(async () =>
            {
                try
                {
                    try { await cts.CancelAsync().ConfigureAwait(false); }
                    finally { await workers.ConfigureAwait(false); }
                }
                finally
                {
                    lock (_serviceSync)
                    {
                        _serviceTask = null;
                        if (!_disposed)
                        {
                            // A normal Stop releases this run's CTS so Start can
                            // create another one. Final shutdown leaves ownership
                            // to DisposeAsync after outbound operations also drain.
                            _cts?.Dispose();
                            _cts = null;
                        }
                    }
                }
            });
            return _stopTask;
        }
    }
}
