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

namespace PDownloader.Runner.Utils;

internal static class ShellProcessLauncher
{
    public static bool OpenFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        object? shell = null;

        try
        {
            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return OpenFileFallback(filePath);
            }

            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return OpenFileFallback(filePath);
            }

            shellType.InvokeMember(
                "ShellExecute",
                System.Reflection.BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args:
                [
                    filePath,
                    string.Empty,
                    Path.GetDirectoryName(filePath) ?? string.Empty,
                    "open",
                    1
                ]);

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ShellProcessLauncher] Shell.Application failed to open '{filePath}': {ex}");
            return OpenFileFallback(filePath);
        }
        finally
        {
            if (shell is not null && Marshal.IsComObject(shell))
            {
                try
                {
                    Marshal.FinalReleaseComObject(shell);
                }
                catch
                {
                    // Ignore COM cleanup failures during application shutdown.
                }
            }
        }
    }

    private static bool OpenFileFallback(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                WorkingDirectory = Path.GetDirectoryName(filePath) ?? string.Empty,
                UseShellExecute = true
            });

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ShellProcessLauncher] Failed to open '{filePath}': {ex}");
            return false;
        }
    }
}
