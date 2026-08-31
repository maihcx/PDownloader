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

namespace PDownloader.Core.Application.App;

/// <summary>
/// Delivers actions to the Main UI, starting it when necessary and retaining
/// actions until Main reports that its IPC endpoint is ready.
/// </summary>
public sealed class MainAppGateway
{
    private readonly CoreIpcHost _ipcHost;
    private readonly object _pendingSync = new();
    private readonly Queue<Func<ConfluxService, Task<bool>>> _pending = new();
    private int _flushActive;
    private int _flushRequested;

    public MainAppGateway(CoreIpcHost ipcHost)
    {
        _ipcHost = ipcHost;
    }

    public void Forward<TPayload>(
        IpcMessageDefinition<TPayload> definition,
        TPayload payload)
    {
        _ = ForwardAsync(definition, payload);
    }

    public async Task ForwardAsync<TPayload>(
        IpcMessageDefinition<TPayload> definition,
        TPayload payload)
    {
        ConfluxService? main = _ipcHost.Main;
        if (main is null)
        {
            return;
        }

        lock (_pendingSync)
        {
            _pending.Enqueue(service => service.SendAsync(definition, payload));
        }

        try
        {
            await main.StartAndWaitUntilReadyAsync().ConfigureAwait(false);
            NotifyReady();
        }
        catch (Exception ex)
        {
            // Keep the queue for the next MainReady/retry; never guess by process name.
            Debug.WriteLine($"[Main gateway] Main is not ready: {ex.Message}");
        }
    }

    /// <summary>
    /// Called when Main sends its startup-ready signal. Pending events are sent
    /// in their original order and retained if delivery still fails.
    /// </summary>
    public void NotifyReady()
    {
        Interlocked.Exchange(ref _flushRequested, 1);
        if (Interlocked.CompareExchange(ref _flushActive, 1, 0) != 0)
        {
            return;
        }

        _ = FlushPendingAsync();
    }

    private async Task FlushPendingAsync()
    {
        Interlocked.Exchange(ref _flushRequested, 0);
        try
        {
            ConfluxService? main = _ipcHost.Main;
            if (main is null)
            {
                return;
            }

            if (!await main.IsReadyAsync().ConfigureAwait(false)) return;

            while (true)
            {
                Func<ConfluxService, Task<bool>>? action;
                lock (_pendingSync)
                {
                    action = _pending.Count > 0 ? _pending.Peek() : null;
                }

                if (action is null)
                {
                    return;
                }

                bool sent;
                try
                {
                    sent = await action(main).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Main gateway] Pending delivery failed: {ex.Message}");
                    sent = false;
                }

                if (!sent)
                {
                    return;
                }

                lock (_pendingSync)
                {
                    if (_pending.Count > 0
                        && ReferenceEquals(_pending.Peek(), action))
                    {
                        _pending.Dequeue();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Main gateway] Flush failed: {ex.Message}");
        }
        finally
        {
            Volatile.Write(ref _flushActive, 0);
            if (Interlocked.Exchange(ref _flushRequested, 0) != 0) NotifyReady();
        }
    }
}
