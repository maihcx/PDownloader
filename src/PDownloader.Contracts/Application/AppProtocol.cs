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

using PDownloader.Contracts.Ipc;

namespace PDownloader.Contracts.Application;

public static class AppProtocol
{
    public static readonly IpcMessageDefinition<MainAppEvent> MainEvent =
        new("app.main-event");

    public static readonly IpcMessageDefinition<TrayNavigationEvent> TrayEvent =
        new("app.tray-event");

    public static readonly IpcMessageDefinition<AppState> State =
        new("app.state");

    public static readonly IpcMessageDefinition<AppState> CoreServiceState =
        new("app.core-service-state");

    public static readonly IpcMessageDefinition<CoreEvent> CoreEventMessage =
        new("app.core-event");
}

public enum AppState
{
    Start,
    Shutdown
}

public enum MainAppEvent
{
    LanguageChanged,
    RadiusChanged,
    MaterialChanged,
    ThemeChanged,
    AppExit
}

public enum TrayNavigationEvent
{
    GoHome,
    GoConfig,
    GoDownload,
    GoSettings,
    GoSettingsUpdate,
    GoAbout
}

public enum CoreEvent
{
    RefreshDownloaderConfigs,
    Ping
}
