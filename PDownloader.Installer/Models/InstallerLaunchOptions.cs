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
    InstallScope? RequestedInstallScope,
    string? RequestedLanguage,
    bool? DesktopShortcut,
    bool? StartMenuShortcut,
    bool? InstallBrowserExtension,
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
            GetInstallScope(args),
            GetOptionValue(args, "--language", "/language"),
            GetOptionalSwitch(
                args,
                "--desktop-shortcut",
                "--no-desktop-shortcut"),
            GetOptionalSwitch(
                args,
                "--start-menu-shortcut",
                "--no-start-menu-shortcut"),
            GetOptionalSwitch(
                args,
                "--browser-extension",
                "--no-browser-extension"),
            GetOptionalSwitch(args, "--run-at-startup", "--no-run-at-startup"),
            HasSwitch(args, "--launch-after-install"));
    }

    public IReadOnlyList<string> ToArguments()
    {
        var arguments = new List<string>();

        if (IsUninstallMode)
        {
            arguments.Add("--uninstall");
        }

        if (IsSilentMode)
        {
            arguments.Add("--silent");
        }

        arguments.Add(RequestedInstallScope switch
        {
            InstallScope.AllUsers => "--all-users",
            _ => "--just-me",
        });

        AddOption(arguments, "--language", RequestedLanguage);
        AddOption(arguments, "--install-dir", InstallDirectory);
        AddOption(arguments, "--update-temp-dir", UpdateTempDirectory);

        AddOptionalSwitch(
            arguments,
            DesktopShortcut,
            "--desktop-shortcut",
            "--no-desktop-shortcut");
        AddOptionalSwitch(
            arguments,
            StartMenuShortcut,
            "--start-menu-shortcut",
            "--no-start-menu-shortcut");
        AddOptionalSwitch(
            arguments,
            InstallBrowserExtension,
            "--browser-extension",
            "--no-browser-extension");
        AddOptionalSwitch(
            arguments,
            RunAtStartup,
            "--run-at-startup",
            "--no-run-at-startup");

        if (LaunchAfterInstall)
        {
            arguments.Add("--launch-after-install");
        }

        return arguments;
    }

    private static InstallScope? GetInstallScope(string[] args)
    {
        if (HasSwitch(args, "--all-users", "/all-users"))
        {
            return InstallScope.AllUsers;
        }

        return HasSwitch(args, "--just-me", "/just-me")
            ? InstallScope.CurrentUser
            : null;
    }

    private static void AddOption(
        ICollection<string> arguments,
        string optionName,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        arguments.Add(optionName);
        arguments.Add(value);
    }

    private static void AddOptionalSwitch(
        ICollection<string> arguments,
        bool? value,
        string enabledOption,
        string disabledOption)
    {
        if (value.HasValue)
        {
            arguments.Add(value.Value ? enabledOption : disabledOption);
        }
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
