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

namespace PDownloader.Core.Application.App;

/// <summary>
/// Relays presentation-level application events between Main, Tray and Runner
/// without exposing IPC routing rules to business services.
/// </summary>
public sealed class AppEventRelay
{
    private readonly CoreIpcHost _ipcHost;
    private readonly RunnerSessionManager _runnerSessions;
    private readonly MainAppGateway _mainGateway;

    public AppEventRelay(
        CoreIpcHost ipcHost,
        RunnerSessionManager runnerSessions,
        MainAppGateway mainGateway)
    {
        _ipcHost = ipcHost;
        _runnerSessions = runnerSessions;
        _mainGateway = mainGateway;
    }

    public void RelayMainEvent(MainAppEvent mainEvent)
    {
        _ipcHost.Tray?.Send(AppProtocol.MainEvent, mainEvent);
        _runnerSessions.Broadcast(AppProtocol.MainEvent, mainEvent);
    }

    public void ForwardTrayEvent(TrayNavigationEvent trayEvent) =>
        _mainGateway.Forward(AppProtocol.TrayEvent, trayEvent);

    public void ForwardMainState(AppState state) =>
        _mainGateway.Forward(AppProtocol.State, state);
}
