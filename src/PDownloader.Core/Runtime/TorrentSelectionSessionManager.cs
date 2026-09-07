// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace PDownloader.Core.Runtime;

public sealed class TorrentSelectionSessionManager : IDisposable
{
    private readonly ConcurrentDictionary<string, TorrentSelectionSession> _sessions =
        new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private int _disposed;
    private bool _stopping;

    public event Action<TorrentSelectionSession>? SessionStarted;

    public async Task<ConfluxService> StartAsync(
        string token,
        TorrentSelectionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentNullException.ThrowIfNull(context);

        TorrentSelectionSession session;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (_stopping)
            {
                throw new InvalidOperationException("Torrent selection sessions are stopping.");
            }

            if (_sessions.ContainsKey(token))
            {
                throw new InvalidOperationException("The torrent selection session already exists.");
            }

            var channel = new ConfluxService { CanMultiple = true };
            channel.Register(
                IpcTopology.TorrentSelectorProcessName,
                IpcTopology.CoreToTorrentSelectorPipeName(token),
                IpcTopology.TorrentSelectorToCorePipeName(token));
            session = new TorrentSelectionSession(token, channel, context);
            channel.TargetExited += processId => { _ = CloseAsync(token); };
            _sessions[token] = session;
            session.StartupTask = Task.Run(() => StartCoreAsync(session));
        }

        return await session.StartupTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ConfluxService> StartCoreAsync(TorrentSelectionSession session)
    {
        try
        {
            SessionStarted?.Invoke(session);
            await session.Channel.StartServiceAsync().ConfigureAwait(false);
            await session.Channel.StartAndWaitUntilReadyAsync(
                $"{TorrentSelectionLaunchProtocol.TokenArgument} {Helpers.Base64Encode(session.Id)}",
                TimeSpan.FromSeconds(20),
                session.Lifetime.Token).ConfigureAwait(false);
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
            if (!_sessions.TryGetValue(id, out TorrentSelectionSession? session))
            {
                return Task.CompletedTask;
            }

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
                    Debug.WriteLine($"[Torrent selector] Close '{id}': {ex.Message}");
                }
                finally
                {
                    lock (_sync)
                    {
                        if (_sessions.TryGetValue(id, out TorrentSelectionSession? current)
                            && ReferenceEquals(current, session))
                        {
                            _sessions.TryRemove(id, out _);
                        }
                    }
                }
            });
            return session.CloseTask;
        }
    }

    public async Task ShutdownAllAsync()
    {
        TorrentSelectionSession[] sessions;
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
                    TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
            catch { }
            finally { await CloseAsync(session.Id).ConfigureAwait(false); }
        })).ConfigureAwait(false);
    }

    public void Broadcast<TPayload>(
        IpcMessageDefinition<TPayload> definition,
        TPayload payload)
    {
        foreach (TorrentSelectionSession session in _sessions.Values.ToArray())
        {
            if (session.StartupTask.IsCompletedSuccessfully
                && session.CloseTask is null)
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
        GC.SuppressFinalize(this);
    }
}
