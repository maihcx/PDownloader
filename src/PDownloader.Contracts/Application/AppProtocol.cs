// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
//
// Copyright (C) Song Mai Software.

namespace PDownloader.Contracts.Application;

/// <summary>
/// Stable application-level messages exchanged between Main, Core, Tray and Runner.
/// These values intentionally preserve the legacy CFS wire format.
/// </summary>
public static class AppProtocol
{
    public const string MainEventMessage = "main-event";
    public const string TrayEventMessage = "tray-event";
    public const string StateMessage = "state";
    public const string CoreServiceStateMessage = "core-svc-state";
    public const string CoreEventMessage = "core-event";

    public static class State
    {
        public const string Start = "start";
        public const string Shutdown = "shutdown";
    }

    public static class MainEvent
    {
        public const string LanguageChanged = "OnLanguageChanged";
        public const string RadiusChanged = "OnRadiusChanged";
        public const string MaterialChanged = "OnMaterialChanged";
        public const string ThemeChanged = "OnThemeChanged";
        public const string AppExit = "OnAppExit";
    }

    public static class TrayEvent
    {
        public const string GoHome = "OnGoHome";
        public const string GoConfig = "OnGoConfig";
        public const string GoDownload = "OnGoDownload";
        public const string GoSettings = "OnGoSettings";
        public const string GoSettingsUpdate = "OnGoSettings--UPDATE";
        public const string GoAbout = "OnGoAbout";
    }

    public static class CoreEvent
    {
        public const string RefreshDownloaderConfigs = "refresh-downloader-configs";
        public const string Ping = "ping";
    }
}
