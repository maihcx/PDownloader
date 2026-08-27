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

namespace PDownloader.Installer.Services.Contracts;

public interface IInstallService
{
    string DefaultInstallPath { get; }

    string AllUsersDefaultInstallPath { get; }

    int EstimatedSize { get; }

    string GetDefaultInstallPath(InstallScope installScope);

    Task InstallAsync(
        string installDir,
        InstallScope installScope,
        bool desktopShortcut,
        bool startMenuShortcut,
        bool installBrowserExtension,
        bool runAtStartup,
        IProgress<(double Percent, string Status)> progress,
        CancellationToken cancellationToken);

    Task UninstallAsync(
        string installDir,
        InstallScope installScope,
        IProgress<(double Percent, string Status)>? progress,
        CancellationToken cancellationToken,
        bool isCleanupExtension = true,
        bool isCleanupUserData = false);

    string? GetInstalledDir(InstallScope installScope);

    InstallScope? GetInstalledScope();

    void ScheduleTemporaryFilesCleanup(
        string? requestedUpdateTempDirectory = null);
}
