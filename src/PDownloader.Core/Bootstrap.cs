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

namespace PDownloader.Core;

public sealed class Bootstrap
{
    private readonly RunnerSessionManager _runnerSessions;
    private readonly DownloadManagerBootstrap _downloadManagerBootstrap;
    private readonly CoreIpcHost _ipcHost;
    private readonly CoreIpcBindings _ipcBindings;
    private readonly DownloadProgressPublisher _progressPublisher;
    private readonly DownloadManager _downloads;

    public Bootstrap(
        RunnerSessionManager runnerSessions,
        DownloadManagerBootstrap downloadManagerBootstrap,
        CoreIpcHost ipcHost,
        CoreIpcBindings ipcBindings,
        DownloadProgressPublisher progressPublisher,
        DownloadManager downloads)
    {
        _runnerSessions = runnerSessions;
        _downloadManagerBootstrap = downloadManagerBootstrap;
        _ipcHost = ipcHost;
        _ipcBindings = ipcBindings;
        _progressPublisher = progressPublisher;
        _downloads = downloads;
    }

    public async Task OnStartedAsync(CancellationToken cancellationToken)
    {
        await _downloadManagerBootstrap.InitializeAsync(cancellationToken).ConfigureAwait(false);

        ConfluxService main = new();
        main.Register(
            IpcTopology.MainProcessName,
            IpcTopology.CoreToMainPipeName,
            IpcTopology.MainToCorePipeName);
        _ipcHost.AttachMain(main);
        _ipcBindings.BindMain(main);
        await main.StartServiceAsync().ConfigureAwait(false);

        ConfluxService tray = new()
        {
            CreateNoWindow = true
        };
        tray.Register(
            IpcTopology.TrayProcessName,
            IpcTopology.CoreToTrayPipeName,
            IpcTopology.TrayToCorePipeName);
        _ipcHost.AttachTray(tray);
        _ipcBindings.BindTray(tray);
        await tray.StartServiceAsync().ConfigureAwait(false);
        await tray.StartAndWaitUntilReadyAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task OnStoppedAsync()
    {
        try
        {
            // Close command admission, stop workers/hash jobs, then persist the
            // final quiescent snapshot before removing UI endpoints.
            await _downloads.DisposeAsync().ConfigureAwait(false);
            await _downloadManagerBootstrap.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            await _progressPublisher.DisposeAsync().ConfigureAwait(false);
            try
            {
                await Task.WhenAll(
                    NotifyShutdownAsync(_ipcHost.Main),
                    NotifyShutdownAsync(_ipcHost.Tray),
                    _runnerSessions.ShutdownAllAsync()).ConfigureAwait(false);
            }
            finally { await _ipcHost.StopAsync().ConfigureAwait(false); }
        }
    }

    private static async Task NotifyShutdownAsync(ConfluxService? endpoint)
    {
        if (endpoint is null)
        {
            return;
        }

        try
        {
            await endpoint.SendAsync(AppProtocol.State, AppState.Shutdown, TimeSpan.FromSeconds(2))
                .ConfigureAwait(false);
        }
        catch (Exception ex) { Debug.WriteLine($"[Bootstrap] UI shutdown: {ex.Message}"); }
    }
}
