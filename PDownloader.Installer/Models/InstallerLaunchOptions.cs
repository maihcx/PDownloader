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
    bool IsSilentMode,
    string? InstallDirectory,
    string? UpdateTempDirectory,
    bool DesktopShortcut,
    bool StartMenuShortcut,
    bool InstallBrowserExtension,
    bool? RunAtStartup,
    bool LaunchAfterInstall)
{
    public static InstallerLaunchOptions Parse(IEnumerable<string> arguments)
    {
        string[] args = arguments.ToArray();

        return new InstallerLaunchOptions(
            HasSwitch(args, "--uninstall", "/uninstall"),
            HasSwitch(args, "--silent", "--quiet", "-s", "/s", "/silent", "/quiet"),
            GetOptionValue(args, "--install-dir", "/dir"),
            GetOptionValue(args, "--update-temp-dir"),
            !HasSwitch(args, "--no-desktop-shortcut"),
            !HasSwitch(args, "--no-start-menu-shortcut"),
            !HasSwitch(args, "--no-browser-extension"),
            GetOptionalSwitch(args, "--run-at-startup", "--no-run-at-startup"),
            HasSwitch(args, "--launch-after-install"));
    }

    private static bool HasSwitch(string[] args, params string[] optionNames) =>
        args.Any(argument => optionNames.Any(optionName =>
            argument.Equals(optionName, StringComparison.OrdinalIgnoreCase)));

    private static bool? GetOptionalSwitch(
        string[] args,
        string enabledOption,
        string disabledOption)
    {
        if (HasSwitch(args, disabledOption))
        {
            return false;
        }

        return HasSwitch(args, enabledOption) ? true : null;
    }

    private static string? GetOptionValue(string[] args, params string[] optionNames)
    {
        for (int index = 0; index < args.Length; index++)
        {
            foreach (string optionName in optionNames)
            {
                if (args[index].Equals(optionName, StringComparison.OrdinalIgnoreCase))
                {
                    return index + 1 < args.Length
                        ? NullIfWhiteSpace(args[index + 1])
                        : null;
                }

                string assignmentPrefix = optionName + "=";
                if (args[index].StartsWith(
                        assignmentPrefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return NullIfWhiteSpace(args[index][assignmentPrefix.Length..]);
                }
            }
        }

        return null;
    }

    private static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
