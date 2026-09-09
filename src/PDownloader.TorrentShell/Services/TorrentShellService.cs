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
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DownloadItemDto> _latestProgress =
        new(StringComparer.Ordinal);
    private ConfluxService? _channel;

    public TorrentShellService(TorrentShellConfig config)
    {
        _config = config;
    }

    public event Action<DownloadItemDto>? ProgressReceived;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var channel = new ConfluxService();
        channel.SetReady(false);
        channel.Register(
            IpcTopology.CoreProcessName,
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
        channel.RegisterMessageHandler(
            AppProtocol.MainEvent,
            TorrentShellCommandHandler.HandleMainEvent);
        channel.RegisterMessageHandler(
            DownloadProtocol.Progress,
            progress =>
            {
                _latestProgress[progress.Id] = progress;
                ProgressReceived?.Invoke(progress);
            });
        channel.RegisterMessageHandler(
            DownloadProtocol.TorrentShellSessionChanged,
            session => Application.Current.Dispatcher.BeginInvoke(
                new Action(() => _config.ApplySession(session))));
        _channel = channel;

        await channel.StartServiceAsync().ConfigureAwait(false);
        await channel.WaitUntilReadyAsync(TimeSpan.FromSeconds(15), cancellationToken)
            .ConfigureAwait(false);

        IpcRequestResult<TorrentShellSessionView> result = await channel.RequestAsync(
            DownloadProtocol.TorrentShellGetSession,
            TimeSpan.FromSeconds(5),
            cancellationToken).ConfigureAwait(false);
        TorrentShellSessionView session = result.Success && result.Value is not null
            ? result.Value
            : throw new IOException(LanguageBase.GetLangValue(
                "torrent_shell_metadata_error",
                result.Error ?? string.Empty));
        _config.ApplySession(session);
    }

    public void SetReady(bool ready) => _channel?.SetReady(ready);

    public bool TryGetLatestProgress(string downloadId, out DownloadItemDto? progress) =>
        _latestProgress.TryGetValue(downloadId, out progress);

    public async Task<TorrentShellStartResult> StartDownloadsAsync(
        IReadOnlyCollection<int> selectedIndexes,
        string saveTo,
        string categoryId,
        bool rememberPathForCategory,
        CancellationToken cancellationToken = default)
    {
        if (_channel is null || selectedIndexes.Count == 0)
        {
            return new TorrentShellStartResult
            {
                Success = false,
                Error = LanguageBase.GetLangValue("torrent_shell_select_one_error")
            };
        }

        IpcRequestResult<TorrentShellStartResult> result = await _channel.RequestAsync(
            DownloadProtocol.TorrentShellStart,
            new TorrentShellStartRequest
            {
                SelectedFileIndexes = selectedIndexes.ToList(),
                SaveTo = saveTo,
                CategoryId = categoryId,
                RememberPathForCategory = rememberPathForCategory
            },
            TimeSpan.FromSeconds(20),
            cancellationToken).ConfigureAwait(false);

        if (!result.Success || result.Value is null)
        {
            return new TorrentShellStartResult
            {
                Success = false,
                Error = result.Error ?? LanguageBase.GetLangValue(
                    "torrent_shell_start_error")
            };
        }

        if (result.Value.Success)
        {
            _config.HasStarted = true;
        }

        return result.Value;
    }

    public void Pause(string downloadId) => SendControl(DownloadProtocol.RunnerPause, downloadId);

    public void Resume(string downloadId) => SendControl(DownloadProtocol.RunnerResume, downloadId);

    public void Retry(string downloadId) => SendControl(DownloadProtocol.RunnerRetry, downloadId);

    public void Cancel(string downloadId) => SendControl(DownloadProtocol.RunnerCancel, downloadId);

    private void SendControl(
        IpcMessageDefinition<DownloadIdRequest> definition,
        string downloadId)
    {
        if (string.IsNullOrWhiteSpace(downloadId))
        {
            return;
        }

        _channel?.Send(
            definition,
            new DownloadIdRequest(downloadId),
            TimeSpan.FromSeconds(30));
    }

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
                _config.HasStarted
                    ? DownloadProtocol.TorrentShellUiClosed
                    : DownloadProtocol.TorrentShellCancelExperience,
                TimeSpan.FromSeconds(1),
                cancellationToken).ConfigureAwait(false);
        }
        catch { }
        finally
        {
            await _channel.StopServiceAsync().ConfigureAwait(false);
        }
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
