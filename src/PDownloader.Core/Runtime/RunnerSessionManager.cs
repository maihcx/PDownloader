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

using PDownloader.Core.Services.DownloadServices;

namespace PDownloader.Core.Runtime;

/// <summary>
/// Owns Runner process/channel lifecycle and the private context associated with
/// each Runner token.
/// </summary>
public sealed class RunnerSessionManager : IDisposable
{
    private readonly DownloadConfigService _downloadConfig;
    private readonly ConcurrentDictionary<string, RunnerSession> _sessions =
        new(StringComparer.Ordinal);
    private int _disposed;
    private bool _stopping;
    private readonly object _sessionSync = new();

    public RunnerSessionManager(DownloadConfigService downloadConfig)
    {
        _downloadConfig = downloadConfig;
    }

    public event Action<RunnerSession>? SessionStarted;
    public event Action<RunnerSession>? SessionReady;

    public async Task<ConfluxService> EnsureStartedAsync(
        string token, RunnerDownloadTask task, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentNullException.ThrowIfNull(task);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RunnerSession session;
            Task? closing;
            lock (_sessionSync)
            {
                ObjectDisposedException.ThrowIf(_disposed != 0, this);
                if (_stopping) throw new InvalidOperationException("Runner sessions are stopping.");
                if (!_sessions.TryGetValue(token, out session!))
                {
                    session = CreateSession(token, task);
                    _sessions[token] = session;
                    // All callers for one token share exactly one startup operation.
                    session.StartupTask = Task.Run(() => StartSessionAsync(session));
                    _ = ObserveStartupAsync(session.StartupTask, token);
                }
                closing = session.CloseTask;
                if (closing is null && session.StartupTask.IsCompletedSuccessfully
                    && !session.Channel.IsAppStarted())
                    closing = CloseSessionAsync(session);
            }
            if (closing is not null)
            {
                await closing.WaitAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }
            // Cancelling a caller's wait does not tear down another caller's session.
            return await session.StartupTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ObserveStartupAsync(Task<ConfluxService> startup, string token)
    {
        try { await startup.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // A browser request may cancel its own wait while shared startup continues.
            Debug.WriteLine($"[Runner session] Startup '{token}' failed: {ex.Message}");
        }
    }

    private RunnerSession CreateSession(string token, RunnerDownloadTask task)
    {
        int threads = task.Threads > 0 ? task.Threads : _downloadConfig.DownloadConfigs.DefaultThreadCount;
        var context = new RunnerDownloadContext
        {
            Url = task.Url, FormatId = task.FormatId, SaveTo = task.SaveTo,
            FileName = task.FileName, Title = task.Title, FileSize = task.FileSize,
            IsRunner = task.IsRunner, Threads = threads > 0 ? threads : 8,
            Headers = NormalizeHeaders(task.Headers)
        };
        var channel = new ConfluxService { CanMultiple = true };
        channel.Register(IpcTopology.RunnerProcessName,
            IpcTopology.CoreToRunnerPipeName(token), IpcTopology.RunnerToCorePipeName(token));
        var session = new RunnerSession(token, channel, context);
        channel.TargetExited += processId => { _ = CloseSessionAsync(session); };
        return session;
    }

    private async Task<ConfluxService> StartSessionAsync(RunnerSession session)
    {
        try
        {
            session.Lifetime.Token.ThrowIfCancellationRequested();
            SessionStarted?.Invoke(session);
            await session.Channel.StartServiceAsync().ConfigureAwait(false);
            // Only an opaque token is exposed on the process command line.
            await session.Channel.StartAndWaitUntilReadyAsync(
                $"{RunnerLaunchProtocol.TokenArgument} {Helpers.Base64Encode(session.Id)}",
                TimeSpan.FromSeconds(20), session.Lifetime.Token).ConfigureAwait(false);
            session.MarkReady();
            SessionReady?.Invoke(session);
            return session.Channel;
        }
        catch
        {
            session.Channel.TryTerminateStartedProcess();
            // Cleanup is independent of StartupTask: it must never await itself.
            await CloseSessionAsync(session).ConfigureAwait(false);
            throw;
        }
    }

    public bool TryGet(string id,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out RunnerSession? session)
    {
        lock (_sessionSync)
        {
            if (_sessions.TryGetValue(id, out session) && session.CloseTask is null)
                return true;
            session = null;
            return false;
        }
    }

    public Task CloseAsync(string id)
    {
        lock (_sessionSync)
            return _sessions.TryGetValue(id, out var session)
                ? CloseSessionAsync(session) : Task.CompletedTask;
    }

    private Task CloseSessionAsync(RunnerSession session)
    {
        lock (_sessionSync)
        {
            if (session.CloseTask is not null) return session.CloseTask;
            session.Lifetime.Cancel();
            session.CloseTask = Task.Run(async () =>
            {
                try { await session.Channel.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Runner session] Close '{session.Id}': {ex.Message}");
                }
                finally
                {
                    lock (_sessionSync)
                    {
                        // An old exit callback must never remove a replacement session.
                        if (_sessions.TryGetValue(session.Id, out var current)
                            && ReferenceEquals(current, session))
                            _sessions.TryRemove(session.Id, out _);
                    }
                    // Lifetime is still read by a possibly unwinding startup task;
                    // let GC reclaim this managed CTS instead of racing Dispose.
                }
            });
            return session.CloseTask;
        }
    }

    public async Task ShutdownAllAsync()
    {
        RunnerSession[] sessions;
        lock (_sessionSync)
        {
            _stopping = true;
            sessions = _sessions.Values.ToArray();
        }
        await Task.WhenAll(sessions.Select(async session =>
        {
            try
            {
                await session.Channel.SendAsync(AppProtocol.State, AppState.Shutdown,
                    TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
            finally { await CloseSessionAsync(session).ConfigureAwait(false); }
        })).ConfigureAwait(false);
    }

    public void Broadcast<TPayload>(IpcMessageDefinition<TPayload> definition, TPayload payload)
    {
        foreach (RunnerSession session in _sessions.Values.ToArray())
        {
            if (session.IsReady && session.CloseTask is null)
                session.Channel.Send(definition, payload);
        }
    }

    private static Dictionary<string, string>? NormalizeHeaders(
        Dictionary<string, string>? headers)
    {
        if (headers is not { Count: > 0 })
        {
            return null;
        }

        var normalized = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string value) in headers)
        {
            if (string.IsNullOrWhiteSpace(key)
                || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            normalized[key] = value;
        }

        return normalized.Count == 0 ? null : normalized;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        lock (_sessionSync)
        {
            _stopping = true;
            foreach (RunnerSession session in _sessions.Values.ToArray())
                _ = CloseSessionAsync(session);
        }

        GC.SuppressFinalize(this);
    }
}
