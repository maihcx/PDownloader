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

namespace PDownloader.Contracts.Ipc;

/// <summary>
/// Process and named-pipe identifiers shared by every PDownloader process.
/// </summary>
public static class IpcTopology
{
    public const string MainProcessName = "PDownloader.exe";
    public const string CoreProcessName = "PDownloader Core.exe";
    public const string TrayProcessName = "PDownloader Tray.exe";
    public const string RunnerProcessName = "PDownloader Runner.exe";

    public const string MainToCorePipeName = "PDownloader.MainToCore";
    public const string CoreToMainPipeName = "PDownloader.CoreToMain";
    public const string TrayToCorePipeName = "PDownloader.TrayToCore";
    public const string CoreToTrayPipeName = "PDownloader.CoreToTray";

    public static string RunnerToCorePipeName(string token) =>
        $"PDownloader.RunnerToCore-{token}";

    public static string CoreToRunnerPipeName(string token) =>
        $"PDownloader.CoreToRunner-{token}";
}
