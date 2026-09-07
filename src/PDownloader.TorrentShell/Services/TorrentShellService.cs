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

namespace PDownloader.TorrentShell.Services;

public sealed class TorrentShellService : IHostedService, IAsyncDisposable
{
    private readonly TorrentShellConfig _config;
    private ConfluxService? _channel;
    private bool _started;

    public TorrentShellService(TorrentShellConfig config) => _config = config;

    public TorrentShellSessionView Session { get; private set; } = new();
    public event Action<DownloadItemDto>? ProgressChanged;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var channel = new ConfluxService();
        channel.SetReady(false);
        channel.Register(IpcTopology.CoreProcessName,
            IpcTopology.TorrentShellToCorePipeName(_config.Token),
            IpcTopology.CoreToTorrentShellPipeName(_config.Token));
        channel.RegisterMessageHandler(AppProtocol.State, state =>
        {
            if (state == AppState.Shutdown)
            {
                Application.Current.Dispatcher.BeginInvoke(
                    new Action(() => Application.Current.Shutdown()));
            }
        });
        channel.RegisterMessageHandler(AppProtocol.MainEvent,
            TorrentShellCommandHandler.HandleMainEvent);
        channel.RegisterMessageHandler(DownloadProtocol.Progress,
            dto => ProgressChanged?.Invoke(dto));
        _channel = channel;

        await channel.StartServiceAsync().ConfigureAwait(false);
        await channel.WaitUntilReadyAsync(TimeSpan.FromSeconds(15), cancellationToken)
            .ConfigureAwait(false);
        IpcRequestResult<TorrentShellSessionView> result = await channel.RequestAsync(
            DownloadProtocol.TorrentShellGetSession,
            TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        Session = result.Success && result.Value is not null
            ? result.Value
            : throw new IOException(LanguageBase.GetLangValue(
                "torrent_selector_metadata_error", result.Error ?? string.Empty));
        _started = Session.IsStarted;
    }

    public void SetReady(bool ready) => _channel?.SetReady(ready);

    public async Task<bool> StartDownloadAsync(TorrentShellStartRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_channel is null || request.SelectedFileIndexes.Count == 0)
        {
            return false;
        }

        bool sent = await _channel.SendAsync(DownloadProtocol.TorrentShellStart, request,
            TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        if (sent)
        {
            _started = true;
        }

        return sent;
    }

    public void Pause() => _channel?.Send(DownloadProtocol.TorrentShellPause, TimeSpan.FromSeconds(30));
    public void Resume() => _channel?.Send(DownloadProtocol.TorrentShellResume, TimeSpan.FromSeconds(30));
    public void Retry() => _channel?.Send(DownloadProtocol.TorrentShellRetry, TimeSpan.FromSeconds(30));
    public void Cancel() => _channel?.Send(DownloadProtocol.TorrentShellCancel, TimeSpan.FromSeconds(30));

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is null)
        {
            return;
        }

        _channel.SetReady(false);
        try
        {
            await _channel.SendAsync(
                _started ? DownloadProtocol.TorrentShellUiClosed
                    : DownloadProtocol.TorrentShellCancelExperience,
                TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
        catch { }

        await _channel.StopServiceAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is null)
        {
            return;
        }

        await _channel.DisposeAsync().ConfigureAwait(false);
        _channel = null;
    }
}
