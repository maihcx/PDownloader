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

namespace PDownloader.TorrentSel.Services;

public sealed class TorrentSelectionService : IHostedService, IAsyncDisposable
{
    private readonly TorrentSelectorConfig _config;
    private ConfluxService? _channel;
    private bool _submitted;

    public TorrentSelectionService(TorrentSelectorConfig config)
    {
        _config = config;
    }

    public TorrentSelectionSessionView Session { get; private set; } = new();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var channel = new ConfluxService();
        channel.SetReady(false);
        channel.Register(
            IpcTopology.CoreProcessName,
            IpcTopology.TorrentSelectorToCorePipeName(_config.Token),
            IpcTopology.CoreToTorrentSelectorPipeName(_config.Token));
        channel.RegisterMessageHandler(AppProtocol.State, state =>
        {
            if (state == AppState.Shutdown)
            {
                Application.Current.Dispatcher.BeginInvoke(
                    new Action(() => Application.Current.Shutdown()));
            }
        });
        channel.RegisterMessageHandler(
            AppProtocol.MainEvent,
            TorrentSelectorCommandHandler.HandleMainEvent);
        _channel = channel;

        await channel.StartServiceAsync().ConfigureAwait(false);
        await channel.WaitUntilReadyAsync(TimeSpan.FromSeconds(15), cancellationToken)
            .ConfigureAwait(false);
        IpcRequestResult<TorrentSelectionSessionView> result = await channel.RequestAsync(
            DownloadProtocol.TorrentSelectionGetSession,
            TimeSpan.FromSeconds(5),
            cancellationToken).ConfigureAwait(false);
        Session = result.Success && result.Value is not null
            ? result.Value
            : throw new IOException(LanguageBase.GetLangValue(
                "torrent_selector_metadata_error",
                result.Error ?? string.Empty));
    }

    public void SetReady(bool ready) => _channel?.SetReady(ready);

    public async Task<bool> ConfirmAsync(
        IReadOnlyCollection<int> selectedIndexes,
        CancellationToken cancellationToken = default)
    {
        if (_channel is null || selectedIndexes.Count == 0)
        {
            return false;
        }

        bool sent = await _channel.SendAsync(
            DownloadProtocol.TorrentSelectionConfirm,
            new TorrentSelectionResult { SelectedFileIndexes = selectedIndexes.ToList() },
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);
        _submitted = sent;
        return sent;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is null)
        {
            return;
        }

        _channel.SetReady(false);
        if (!_submitted)
        {
            try
            {
                await _channel.SendAsync(
                    DownloadProtocol.TorrentSelectionCancel,
                    TimeSpan.FromSeconds(1),
                    cancellationToken).ConfigureAwait(false);
            }
            catch { }
        }

        await _channel.StopServiceAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync().ConfigureAwait(false);
            _channel = null;
        }
    }
}
