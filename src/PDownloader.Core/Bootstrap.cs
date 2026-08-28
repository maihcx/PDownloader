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

using Microsoft.Extensions.Hosting;

namespace PDownloader.Core;

public class Bootstrap
{
    private readonly IHostApplicationLifetime lifetime;

    public Bootstrap(IHostApplicationLifetime lifetime)
    {
        this.lifetime = lifetime;
    }

    public void OnStarted()
    {
        DownloadRuntime.Configure(new DownloadRuntimeOptions
        {
            GetDefaultDownloadFolder = () => CFSCommandHandler.DownloadConfigService.DownloadConfigs?.DefaultDownloadFolder,
            GetDefaultTempFolder = () => CFSCommandHandler.DownloadConfigService.DownloadConfigs?.DefaultTempFolder,
            GetFallbackDownloadFolder = Helpers.GetDefaultFolder,
            ShowRunner = (id, task) => _ = DownloadRunner.EnsureRunnerStarted(id, task)
        });

        // Wire up download manager broadcasts
        DownloadManagerBootstrap.InitDownloadManager();

        #region ConfluxService — PDownloader.exe (Main UI)
        ConfluxService cfsMain = new();
        cfsMain.Register(
            IpcTopology.MainProcessName,
            IpcTopology.CoreToMainPipeName,
            IpcTopology.MainToCorePipeName);
        AppRuntime.cfsMain = cfsMain;
        cfsMain.OnMessageReceived += CFSCommandHandler.Handle;
        cfsMain.RegisterRequestHandler(
            DownloadProtocol.GetList,
            () => DownloadManager.Instance.GetContractList());
        cfsMain.RegisterRequestHandler(
            UpdateProtocol.GetState,
            () => Program.GetRequiredService<CoreUpdateCoordinator>().GetStateSnapshot());
        _ = cfsMain.StartServiceAsync();
        #endregion

        #region ConfluxService — PDownloader Tray.exe
        ConfluxService cfsTray = new();
        cfsTray.Register(
            IpcTopology.TrayProcessName,
            IpcTopology.CoreToTrayPipeName,
            IpcTopology.TrayToCorePipeName);
        AppRuntime.cfsTray = cfsTray;
        cfsTray.OnMessageReceived += CFSCommandHandler.Handle;
        cfsTray.RegisterRequestHandler(
            UpdateProtocol.GetState,
            () => Program.GetRequiredService<CoreUpdateCoordinator>().GetStateSnapshot());
        cfsTray.CreateNoWindow = true;
        _ = cfsTray.StartServiceAsync();
        cfsTray.StartApp();
        #endregion

        // Runner is started on-demand when first download request arrives
    }

    public void OnStopped()
    {
        _ = AppRuntime.cfsMain?.StopServiceAsync();
        _ = AppRuntime.cfsTray?.StopServiceAsync();

        AppRuntime.cfsMain = AppRuntime.cfsTray = null;
    }

    public void Shutdown() => lifetime.StopApplication();
}
