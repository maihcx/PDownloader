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

using System.Diagnostics;
using System.Security.Principal;

namespace PDownloader.Installer.Services;

public sealed class InstallerApplicationService : IInstallerApplicationService
{
    public bool IsAdministrator
    {
        get
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public void Shutdown(int exitCode = 0)
    {
        System.Windows.Application.Current.Shutdown(exitCode);
    }

    public bool TryLaunch(string executablePath, string workingDirectory)
    {
        return File.Exists(executablePath)
            && UnelevatedProcessLauncher.TryStart(
                executablePath,
                workingDirectory);
    }

    public async Task<int?> RunElevatedAsync(
        IReadOnlyCollection<string> arguments,
        CancellationToken cancellationToken)
    {
        string? installerPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
        {
            return null;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                WorkingDirectory = Path.GetDirectoryName(installerPath)
                    ?? AppContext.BaseDirectory,
                UseShellExecute = true,
                Verb = "runas",
            };

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode;
        }
        catch
        {
            // This includes the user declining the UAC consent prompt.
            return null;
        }
    }
}
