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

using PDownloader.Contracts.Ipc;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace PDownloader.CFS;

public sealed partial class ConfluxService
{
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private volatile bool _ready = true;
    private bool _hasLaunched;

    /// <summary>Raised only for the exact process tracked by this endpoint.</summary>
    public event Action<int>? TargetExited;

    public void SetReady(bool ready) => _ready = ready;

    private IpcEndpointHealth GetLocalHealth() => new(
        _ready && !_disposed && _cts is { IsCancellationRequested: false },
        Environment.ProcessId, _instanceId, ReceivePipeName,
        Environment.ProcessPath ?? string.Empty);

    /// <summary>Starts and tracks a process. This alone does not establish readiness.</summary>
    public Process StartProcess(string arguments = "")
    {
        lock (_processSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsTrackedProcessAlive())
                return _currProcess!;

            string path = ResolveProcessPath();
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = false,
                Arguments = arguments,
                CreateNoWindow = CreateNoWindow,
                WorkingDirectory = Path.GetDirectoryName(path) ?? AppContext.BaseDirectory
            }) ?? throw new IOException($"Could not start '{path}'.");

            _hasLaunched = true;
            ReplaceCurrentProcess(process);
            return process; // Borrowed handle; ConfluxService owns its lifetime.
        }
    }

    /// <summary>Kept for synchronous callers. Prefer the async lifecycle API.</summary>
    public bool StartApp(string argEnvironment = "")
    {
        try
        {
            StartAndWaitUntilReadyAsync(argEnvironment).GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CFS] Startup failed: {ex.Message}");
            return false;
        }
    }

    public async Task<IpcEndpointHealth> StartAndWaitUntilReadyAsync(
        string arguments = "", TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        using OperationLease operation = TryBeginOperation()
            ?? throw new ObjectDisposedException(nameof(ConfluxService));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, operation.Token);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(15));
        bool entered = false;
        try
        {
            await _startGate.WaitAsync(deadline.Token).ConfigureAwait(false);
            entered = true;
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (CanMultiple)
            {
                // Private sessions must bind to our new child, never adopt an old
                // Runner that still has the same token during its shutdown window.
                if (!IsAppStarted()) StartProcess(arguments);
                return await WaitUntilReadyAsync(timeout, deadline.Token).ConfigureAwait(false);
            }
            // Before launching a singleton, only probe an existing listener.
            // Waiting for a missing pipe here would delay Process.Start by the
            // full probe timeout on every cold launch from Tray.
            var health = await GetHealthCoreAsync(TimeSpan.FromMilliseconds(300),
                deadline.Token, connectImmediately: true)
                .ConfigureAwait(false);
            if (health is { Ready: true })
                return health;

            // A reachable but initializing endpoint must not cause another launch.
            if (health is null && !IsAppStarted())
                StartProcess(arguments);

            return await WaitUntilReadyAsync(timeout, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            throw new ObjectDisposedException(nameof(ConfluxService));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Endpoint '{SendPipeName}' did not become ready.");
        }
        finally
        {
            if (entered) _startGate.Release();
        }
    }

    public async Task<IpcEndpointHealth> WaitUntilReadyAsync(
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        using OperationLease operation = TryBeginOperation()
            ?? throw new ObjectDisposedException(nameof(ConfluxService));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, operation.Token);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(15));
        try
        {
            while (true)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                deadline.Token.ThrowIfCancellationRequested();
                if (TrackedSessionExited())
                    throw new IOException("The tracked Runner exited before becoming ready.");

                var health = await GetHealthAsync(TimeSpan.FromMilliseconds(500), deadline.Token)
                    .ConfigureAwait(false);
                if (health is { Ready: true })
                    return health;
                await Task.Delay(100, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            throw new ObjectDisposedException(nameof(ConfluxService));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Endpoint '{SendPipeName}' did not become ready.");
        }
    }

    public Task<IpcEndpointHealth?> GetHealthAsync(
        TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        GetHealthCoreAsync(timeout, cancellationToken, connectImmediately: false);

    private async Task<IpcEndpointHealth?> GetHealthCoreAsync(
        TimeSpan? timeout, CancellationToken cancellationToken, bool connectImmediately)
    {
        // Health must not queue behind application sends or command execution.
        var result = await RequestCoreAsync(IpcHealthProtocol.Get, new IpcNoPayload(),
            timeout ?? TimeSpan.FromMilliseconds(500), cancellationToken,
            serialize: false, connectImmediately: connectImmediately)
            .ConfigureAwait(false);
        if (!result.Success || result.Value is not { } health
            || health.ProcessId <= 0 || string.IsNullOrWhiteSpace(health.InstanceId)
            || health.Endpoint != SendPipeName
            || !string.Equals(Path.GetFullPath(health.ExecutablePath), ResolveProcessPath(),
                StringComparison.OrdinalIgnoreCase))
            return null;
        return health;
    }

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default) =>
        await GetHealthAsync(cancellationToken: cancellationToken).ConfigureAwait(false)
            is { Ready: true };

    /// <summary>Only the tracked handle, never a process-name lookup or readiness check.</summary>
    public bool IsAppStarted()
    {
        lock (_processSync) return IsTrackedProcessAlive();
    }

    public Process GetProcess()
    {
        lock (_processSync)
            return IsTrackedProcessAlive() ? _currProcess!
                : throw new InvalidOperationException("No live process is tracked by this endpoint.");
    }

    /// <summary>Only for a failed child startup; never kills by name or an adopted process.</summary>
    public void TryTerminateStartedProcess()
    {
        lock (_processSync)
        {
            if (!CanMultiple || !_hasLaunched || !IsTrackedProcessAlive()) return;
            try { _currProcess!.Kill(); }
            catch (Exception ex) { Debug.WriteLine($"[CFS] Child cleanup failed: {ex.Message}"); }
        }
    }

    private bool IsTrackedProcessAlive()
    {
        try { return _currProcess is not null && !_currProcess.HasExited; }
        catch (InvalidOperationException) { return false; }
    }

    private bool TrackedSessionExited()
    {
        lock (_processSync) return CanMultiple && _hasLaunched && !IsTrackedProcessAlive();
    }

    private string ResolveProcessPath() => Path.GetFullPath(
        Path.IsPathFullyQualified(ProcessPackage) ? ProcessPackage
            : Path.Combine(AppContext.BaseDirectory, ProcessPackage));

    private void ReplaceCurrentProcess(Process? process)
    {
        if (ReferenceEquals(_currProcess, process)) return;
        if (_currProcess is not null)
        {
            _currProcess.Exited -= OnTargetExited;
            _currProcess.Dispose();
        }
        _currProcess = process;
        if (process is not null)
        {
            process.Exited += OnTargetExited;
            process.EnableRaisingEvents = true;
        }
    }

    private void OnTargetExited(object? sender, EventArgs args)
    {
        int id;
        lock (_processSync)
        {
            if (_disposed || sender is not Process process
                || !ReferenceEquals(process, _currProcess)) return;
            id = process.Id;
        }
        // EnableRaisingEvents may raise Exited synchronously inside StartProcess.
        // Do not call application lifecycle code while that outer process lock is held.
        var handlers = TargetExited;
        _ = Task.Run(() =>
        {
            try { handlers?.Invoke(id); }
            catch (Exception ex) { Debug.WriteLine($"[CFS] Exit observer failed: {ex}"); }
        });
    }

    private void ValidateServerProcess(NamedPipeClientStream pipe)
    {
        if (!NativeMethods.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint rawPid))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        int pid = checked((int)rawPid);
        lock (_processSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (CanMultiple && _hasLaunched)
            {
                if (!IsTrackedProcessAlive() || _currProcess!.Id != pid)
                    throw new IOException("Pipe does not belong to the tracked Runner.");
                return;
            }
            if (IsTrackedProcessAlive() && _currProcess!.Id == pid) return;

            Process process = Process.GetProcessById(pid);
            try
            {
                string? actualPath = process.MainModule?.FileName;
                if (actualPath is null || !string.Equals(Path.GetFullPath(actualPath),
                        ResolveProcessPath(), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Pipe belongs to a different application installation.");
                ReplaceCurrentProcess(process);
            }
            catch
            {
                if (!ReferenceEquals(_currProcess, process)) process.Dispose();
                throw;
            }
        }
    }

}
