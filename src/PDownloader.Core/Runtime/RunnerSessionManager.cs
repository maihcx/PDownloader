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

    public RunnerSessionManager(DownloadConfigService downloadConfig)
    {
        _downloadConfig = downloadConfig;
    }

    public event Action<RunnerSession>? SessionStarted;

    public ConfluxService? EnsureStarted(string token, RunnerDownloadTask task)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentNullException.ThrowIfNull(task);

        if (_sessions.TryGetValue(token, out RunnerSession? existing))
        {
            return existing.Channel;
        }

        int threads = task.Threads > 0
            ? task.Threads
            : _downloadConfig.DownloadConfigs.DefaultThreadCount;
        if (threads <= 0)
        {
            threads = 8;
        }

        RunnerDownloadContext context = new()
        {
            Url = task.Url,
            FormatId = task.FormatId,
            SaveTo = task.SaveTo,
            FileName = task.FileName,
            Title = task.Title,
            FileSize = task.FileSize,
            IsRunner = task.IsRunner,
            Threads = threads,
            Headers = NormalizeHeaders(task.Headers)
        };

        var channel = new ConfluxService
        {
            CanMultiple = true
        };
        channel.Register(
            IpcTopology.RunnerProcessName,
            IpcTopology.CoreToRunnerPipeName(token),
            IpcTopology.RunnerToCorePipeName(token));

        var session = new RunnerSession(token, channel, context);
        if (!_sessions.TryAdd(token, session))
        {
            channel.Dispose();
            return _sessions.TryGetValue(token, out existing)
                ? existing.Channel
                : null;
        }

        try
        {
            SessionStarted?.Invoke(session);
            _ = channel.StartServiceAsync();

            // Only the opaque token is exposed through the process command line.
            bool started = channel.StartApp(
                $"{RunnerLaunchProtocol.TokenArgument} {Helpers.Base64Encode(token)}");
            if (!started)
            {
                throw new InvalidOperationException(
                    $"Failed to start Runner process for session '{token}'.");
            }

            return channel;
        }
        catch
        {
            _sessions.TryRemove(token, out _);
            channel.Dispose();
            throw;
        }
    }

    public bool TryGet(
        string id,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out RunnerSession? session) =>
        _sessions.TryGetValue(id, out session);

    public async Task CloseAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id)
            || !_sessions.TryRemove(id, out RunnerSession? session))
        {
            return;
        }

        try
        {
            await session.Channel.StopServiceAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Runner session] Failed to stop '{id}': {ex.Message}");
        }
        finally
        {
            session.Channel.Dispose();
        }
    }

    public async Task ShutdownAllAsync()
    {
        RunnerSession[] sessions = _sessions.Values.ToArray();
        foreach (RunnerSession session in sessions)
        {
            try
            {
                session.Channel.Send(
                    AppProtocol.State,
                    AppState.Shutdown,
                    TimeSpan.FromSeconds(1));
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[Runner session] Failed to notify '{session.Id}' of shutdown: {ex.Message}");
            }
        }

        await Task.WhenAll(
            sessions.Select(session => CloseAsync(session.Id)))
            .ConfigureAwait(false);
    }

    public void Broadcast<TPayload>(
        IpcMessageDefinition<TPayload> definition,
        TPayload payload)
    {
        foreach (RunnerSession session in _sessions.Values.ToArray())
        {
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

        RunnerSession[] sessions = _sessions.Values.ToArray();
        _sessions.Clear();
        foreach (RunnerSession session in sessions)
        {
            session.Channel.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
