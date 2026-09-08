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

namespace PDownloader.Core.Application.Downloads;

/// <summary>
/// Projects download state into per-client mailboxes. Publication never connects
/// to a pipe or waits for a UI acknowledgement; each client owns its sender loop.
/// </summary>
public sealed class DownloadProgressPublisher : IAsyncDisposable
{
    private readonly DownloadManager _downloads;
    private readonly object _sync = new();
    private readonly Dictionary<string, ProgressClientSender> _runners = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TorrentShellTarget> _torrentShells = new(StringComparer.Ordinal);
    private readonly HashSet<ProgressClientSender> _ownedSenders = new();
    private ProgressClientSender? _main;
    private Task? _stopTask;

    public DownloadProgressPublisher(DownloadManager downloads)
    {
        _downloads = downloads;
    }

    public void Publish(DownloadItem item)
    {
        lock (_sync)
        {
            if (_stopTask is not null)
            {
                return;
            }

            _runners.TryGetValue(item.Id, out ProgressClientSender? runner);
            TorrentShellTarget[] shells = _torrentShells.Values
                .Where(target => target.Session.OwnsDownload(item.Id))
                .ToArray();
            if (_main is null && runner is null && shells.Length == 0)
            {
                return;
            }

            DownloadItemDto snapshot = DownloadManager.ToContract(item);
            _main?.Publish(snapshot);
            runner?.Publish(snapshot);
            foreach (TorrentShellTarget shell in shells)
            {
                shell.Sender.Publish(snapshot);
            }
        }
    }

    public void AttachMain(ConfluxService channel)
    {
        int processId;
        try { processId = channel.GetProcess().Id; }
        catch (InvalidOperationException) { return; }

        lock (_sync)
        {
            if (_stopTask is not null || _main?.Matches(channel, processId) == true)
            {
                return;
            }

            if (_main is not null)
            {
                _ = _main.DisposeAsync();
            }

            var sender = new ProgressClientSender(channel, processId);
            _main = sender;
            Track(sender, runnerId: null, torrentShellId: null);
            foreach (DownloadItem item in _downloads.GetAll())
            {
                sender.Publish(DownloadManager.ToContract(item));
            }
        }
    }

    public void AttachRunner(RunnerSession session)
    {
        if (!session.IsReady || session.Lifetime.IsCancellationRequested)
        {
            return;
        }

        int processId;
        try { processId = session.Channel.GetProcess().Id; }
        catch (InvalidOperationException) { return; }

        lock (_sync)
        {
            if (_stopTask is not null || session.Lifetime.IsCancellationRequested)
            {
                return;
            }

            if (_runners.TryGetValue(session.Id, out ProgressClientSender? previous))
            {
                if (previous.Matches(session.Channel, processId))
                {
                    return;
                }

                _ = previous.DisposeAsync();
            }

            var sender = new ProgressClientSender(
                session.Channel,
                processId,
                session.Lifetime.Token);
            _runners[session.Id] = sender;
            Track(sender, runnerId: session.Id, torrentShellId: null);
            if (_downloads.Find(session.Id) is { } item)
            {
                sender.Publish(DownloadManager.ToContract(item));
            }
        }
    }

    public void AttachTorrentShell(TorrentShellSession session)
    {
        if (session.Lifetime.IsCancellationRequested)
        {
            return;
        }

        int processId;
        try { processId = session.Channel.GetProcess().Id; }
        catch (InvalidOperationException) { return; }

        lock (_sync)
        {
            if (_stopTask is not null || session.Lifetime.IsCancellationRequested)
            {
                return;
            }

            if (_torrentShells.TryGetValue(session.Id, out TorrentShellTarget? previous))
            {
                if (previous.Sender.Matches(session.Channel, processId))
                {
                    return;
                }

                _ = previous.Sender.DisposeAsync();
            }

            var sender = new ProgressClientSender(
                session.Channel,
                processId,
                session.Lifetime.Token);
            _torrentShells[session.Id] = new TorrentShellTarget(session, sender);
            Track(sender, runnerId: null, torrentShellId: session.Id);
            foreach (string downloadId in session.GetDownloadIds())
            {
                if (_downloads.Find(downloadId) is { } item)
                {
                    sender.Publish(DownloadManager.ToContract(item));
                }
            }
        }
    }

    private void Track(
        ProgressClientSender sender,
        string? runnerId,
        string? torrentShellId)
    {
        _ownedSenders.Add(sender);
        _ = ObserveSenderAsync(sender, runnerId, torrentShellId);
    }

    private async Task ObserveSenderAsync(
        ProgressClientSender sender,
        string? runnerId,
        string? torrentShellId)
    {
        try
        {
            await sender.Completion.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Progress] Sender stopped: {ex.Message}");
        }
        finally
        {
            await sender.DisposeAsync().ConfigureAwait(false);
            lock (_sync)
            {
                _ownedSenders.Remove(sender);
                if (runnerId is null && torrentShellId is null)
                {
                    if (ReferenceEquals(_main, sender))
                    {
                        _main = null;
                    }
                }
                else if (runnerId is not null
                    && _runners.TryGetValue(runnerId, out ProgressClientSender? current)
                    && ReferenceEquals(current, sender))
                {
                    _runners.Remove(runnerId);
                }

                if (torrentShellId is not null
                    && _torrentShells.TryGetValue(torrentShellId, out TorrentShellTarget? shell)
                    && ReferenceEquals(shell.Sender, sender))
                {
                    _torrentShells.Remove(torrentShellId);
                }
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        lock (_sync)
        {
            if (_stopTask is null)
            {
                ProgressClientSender[] senders = _ownedSenders.ToArray();
                _main?.DisposeAsync();
                _main = null;
                _runners.Clear();
                _torrentShells.Clear();
                _stopTask = Task.Run(async () =>
                {
                    await Task.WhenAll(senders.Select(sender => sender.DisposeAsync().AsTask()))
                        .ConfigureAwait(false);
                });
            }

            return new ValueTask(_stopTask);
        }
    }

    private sealed record TorrentShellTarget(
        TorrentShellSession Session,
        ProgressClientSender Sender);
}
