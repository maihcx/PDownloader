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

namespace PDownloader.Core.Runtime;

public sealed class TorrentShellSessionManager : IDisposable
{
    private static readonly TimeSpan ShutdownSignalTimeout =
        TimeSpan.FromMilliseconds(150);

    private readonly ConcurrentDictionary<string, TorrentShellSession> _sessions =
        new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private int _disposed;
    private bool _stopping;

    public event Action<TorrentShellSession>? SessionStarted;
    public event Action<TorrentShellSession>? SessionReady;

    public async Task<TorrentShellSession> StartAsync(
        string token,
        TorrentShellContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentNullException.ThrowIfNull(context);

        TorrentShellSession session;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (_stopping)
            {
                throw new InvalidOperationException("TorrentShell sessions are stopping.");
            }

            if (_sessions.ContainsKey(token))
            {
                throw new InvalidOperationException("The TorrentShell session already exists.");
            }

            session = CreateSession(token, context, hasStarted: false);
            _sessions[token] = session;
            session.StartupTask = Task.Run(() => StartCoreAsync(session));
        }

        await session.StartupTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        return session;
    }

    public async Task<TorrentShellSession> EnsureStartedAsync(
        string token,
        TorrentShellContext context,
        IEnumerable<string> downloadIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(downloadIds);
        string[] ownedDownloadIds = downloadIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TorrentShellSession session;
            Task? closing;
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed != 0, this);
                if (_stopping)
                {
                    throw new InvalidOperationException("TorrentShell sessions are stopping.");
                }

                if (!_sessions.TryGetValue(token, out session!))
                {
                    session = CreateSession(token, context, hasStarted: true);
                    session.SetDownloadIds(ownedDownloadIds);
                    _sessions[token] = session;
                    session.StartupTask = Task.Run(() => StartCoreAsync(session));
                }
                else
                {
                    session.MarkStarted();
                    session.SetDownloadIds(ownedDownloadIds);
                }

                closing = session.CloseTask;
                if (closing is null
                    && session.StartupTask.IsCompletedSuccessfully
                    && !session.Channel.IsAppStarted())
                {
                    closing = CloseSessionAsync(session);
                }
            }

            if (closing is not null)
            {
                await closing.WaitAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            await session.StartupTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            return session;
        }
    }

    public TorrentShellSession? FindByDownloadId(string downloadId)
    {
        lock (_sync)
        {
            return _sessions.Values.FirstOrDefault(session =>
                session.OwnsDownload(downloadId));
        }
    }

    private TorrentShellSession CreateSession(
        string token,
        TorrentShellContext context,
        bool hasStarted)
    {
        var channel = new ConfluxService { CanMultiple = true };
        channel.Register(
            IpcTopology.TorrentShellProcessName,
            IpcTopology.CoreToTorrentShellPipeName(token),
            IpcTopology.TorrentShellToCorePipeName(token));
        var session = new TorrentShellSession(token, channel, context, hasStarted);
        channel.TargetExited += processId => { _ = CloseAsync(token); };
        return session;
    }

    private async Task<ConfluxService> StartCoreAsync(TorrentShellSession session)
    {
        try
        {
            SessionStarted?.Invoke(session);
            await session.Channel.StartServiceAsync().ConfigureAwait(false);
            await session.Channel.StartAndWaitUntilReadyAsync(
                $"{TorrentShellLaunchProtocol.TokenArgument} {Helpers.Base64Encode(session.Id)}",
                TimeSpan.FromSeconds(20),
                session.Lifetime.Token).ConfigureAwait(false);
            session.MarkReady();
            SessionReady?.Invoke(session);
            return session.Channel;
        }
        catch
        {
            session.Channel.TryTerminateStartedProcess();
            await CloseAsync(session.Id).ConfigureAwait(false);
            throw;
        }
    }

    public Task CloseAsync(string id)
    {
        lock (_sync)
        {
            return _sessions.TryGetValue(id, out TorrentShellSession? session)
                ? CloseSessionAsync(session)
                : Task.CompletedTask;
        }
    }

    private Task CloseSessionAsync(TorrentShellSession session)
    {
        lock (_sync)
        {
            if (session.CloseTask is not null)
            {
                return session.CloseTask;
            }

            session.Lifetime.Cancel();
            session.CloseTask = Task.Run(async () =>
            {
                try { await session.Channel.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[TorrentShell] Close '{session.Id}': {ex.Message}");
                }
                finally
                {
                    lock (_sync)
                    {
                        if (_sessions.TryGetValue(
                                session.Id,
                                out TorrentShellSession? current)
                            && ReferenceEquals(current, session))
                        {
                            _sessions.TryRemove(session.Id, out _);
                        }
                    }
                }
            });
            return session.CloseTask;
        }
    }

    public async Task ShutdownAllAsync()
    {
        TorrentShellSession[] sessions;
        lock (_sync)
        {
            _stopping = true;
            sessions = _sessions.Values.ToArray();
        }

        await Task.WhenAll(sessions.Select(async session =>
        {
            try
            {
                await session.Channel.SendAsync(
                    AppProtocol.State,
                    AppState.Shutdown,
                    ShutdownSignalTimeout).ConfigureAwait(false);
            }
            catch { }
            finally { await CloseAsync(session.Id).ConfigureAwait(false); }
        })).ConfigureAwait(false);
    }

    public void Broadcast<TPayload>(
        IpcMessageDefinition<TPayload> definition,
        TPayload payload)
    {
        foreach (TorrentShellSession session in _sessions.Values.ToArray())
        {
            if (session.IsReady && session.CloseTask is null)
            {
                session.Channel.Send(definition, payload);
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        SessionStarted = null;
        SessionReady = null;
        GC.SuppressFinalize(this);
    }
}
