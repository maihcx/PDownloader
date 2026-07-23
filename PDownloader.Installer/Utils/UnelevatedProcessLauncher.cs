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
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PDownloader.Installer.Utils;

internal static class UnelevatedProcessLauncher
{
    private const uint TokenAssignPrimary = 0x0001;
    private const uint TokenDuplicate = 0x0002;
    private const uint TokenQuery = 0x0008;
    private const uint LogonWithProfile = 0x00000001;

    public static bool TryStart(string fileName, string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(fileName) || !File.Exists(fileName))
        {
            return false;
        }

        if (TryStartWithShellToken(fileName, workingDirectory))
        {
            return true;
        }

        return TryStartViaExplorer(fileName);
    }

    private static bool TryStartWithShellToken(string fileName, string? workingDirectory)
    {
        nint shellWindow = NativeMethods.GetShellWindow();
        if (shellWindow == nint.Zero)
        {
            return false;
        }

        _ = NativeMethods.GetWindowThreadProcessId(shellWindow, out uint shellProcessId);
        if (shellProcessId == 0)
        {
            return false;
        }

        nint shellToken = nint.Zero;
        NativeMethods.PROCESS_INFORMATION processInfo = default;

        try
        {
            using Process shellProcess = Process.GetProcessById((int)shellProcessId);

            const uint desiredAccess = TokenAssignPrimary | TokenDuplicate | TokenQuery;
            if (!NativeMethods.OpenProcessToken(shellProcess.Handle, desiredAccess, out shellToken))
            {
                return false;
            }

            NativeMethods.STARTUPINFO startupInfo = new()
            {
                cb = Marshal.SizeOf<NativeMethods.STARTUPINFO>(),
            };

            string commandLine = $"\"{fileName}\"";
            var mutableCommandLine = new StringBuilder(commandLine);

            bool started = NativeMethods.CreateProcessWithTokenW(
                shellToken,
                LogonWithProfile,
                fileName,
                mutableCommandLine,
                0,
                nint.Zero,
                string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory,
                ref startupInfo,
                out processInfo);

            return started;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (processInfo.hThread != nint.Zero)
            {
                NativeMethods.CloseHandle(processInfo.hThread);
            }

            if (processInfo.hProcess != nint.Zero)
            {
                NativeMethods.CloseHandle(processInfo.hProcess);
            }

            if (shellToken != nint.Zero)
            {
                NativeMethods.CloseHandle(shellToken);
            }
        }
    }

    private static bool TryStartViaExplorer(string fileName)
    {
        try
        {
            string explorerPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "explorer.exe");

            var startInfo = new ProcessStartInfo
            {
                FileName = explorerPath,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add(fileName);

            return Process.Start(startInfo) is not null;
        }
        catch
        {
            return false;
        }
    }
}
