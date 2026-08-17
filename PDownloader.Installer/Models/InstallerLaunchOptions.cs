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

namespace PDownloader.Installer.Models;

public sealed record InstallerLaunchOptions(
    bool IsUninstallMode,
    string? UpdateTempDirectory)
{
    public static InstallerLaunchOptions Parse(IEnumerable<string> arguments)
    {
        string[] args = arguments.ToArray();

        return new InstallerLaunchOptions(
            args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase),
            GetOptionValue(args, "--update-temp-dir"));
    }

    private static string? GetOptionValue(string[] args, string optionName)
    {
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (args[index].Equals(optionName, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}
