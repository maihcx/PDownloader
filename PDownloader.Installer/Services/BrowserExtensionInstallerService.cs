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

using Microsoft.Win32;
using System.IO;

namespace PDownloader.Installer.Services;

public static class BrowserExtensionInstallerService
{
    public const string ExtensionId = "nliblbkhgljcpdboininiepogjaegien";

    private const string ExtensionSettingsSubKey = "ExtensionSettings";

    private const string UpdateURI = "https://raw.githubusercontent.com/maihcx/PDownloader/main/BrowserExtension/update.xml";

    private static readonly (string DisplayName, string PolicyRoot)[] SupportedBrowsers =
    {
        ("Google Chrome",   @"SOFTWARE\Policies\Google\Chrome"),
        ("Microsoft Edge",  @"SOFTWARE\Policies\Microsoft\Edge"),
        ("Brave",           @"SOFTWARE\Policies\BraveSoftware\Brave"),
        ("Cốc Cốc",         @"SOFTWARE\Policies\CocCoc\CocCoc"),
    };

    public static void InstallForAllBrowsers(string installDir)
    {
        if (string.IsNullOrWhiteSpace(ExtensionId) ||
            ExtensionId.StartsWith("REPLACE_", StringComparison.Ordinal))
        {
            return;
        }

        foreach ((string _, string policyRoot) in SupportedBrowsers)
        {
            try
            {
                RegisterExtensionPolicy(policyRoot, UpdateURI);
            }
            catch
            {
                // Ignore unsupported browser policy
            }
        }
    }

    public static void UninstallForAllBrowsers()
    {
        if (string.IsNullOrWhiteSpace(ExtensionId) ||
            ExtensionId.StartsWith("REPLACE_", StringComparison.Ordinal))
        {
            return;
        }

        foreach ((string _, string policyRoot) in SupportedBrowsers)
        {
            try
            {
                RemoveExtensionPolicy(policyRoot);
            }
            catch
            {
            }
        }
    }

    private static void RegisterExtensionPolicy(
        string policyRoot,
        string updateUrl)
    {
        using RegistryKey? extensionSettings =
            Registry.LocalMachine.CreateSubKey(
                $"{policyRoot}\\{ExtensionSettingsSubKey}",
                writable: true);

        if (extensionSettings == null)
        {
            return;
        }

        using RegistryKey? extension =
            extensionSettings.CreateSubKey(
                ExtensionId,
                writable: true);

        if (extension == null)
        {
            return;
        }

        extension.SetValue(
            "installation_mode",
            "force_installed",
            RegistryValueKind.String);

        extension.SetValue(
            "toolbar_pin",
            "default_unpinned",
            RegistryValueKind.String);

        extension.SetValue(
            "update_url",
            updateUrl,
            RegistryValueKind.String);

        extension.SetValue(
            "override_update_url",
            1,
            RegistryValueKind.DWord);
    }

    private static void RemoveExtensionPolicy(string policyRoot)
    {
        using RegistryKey? extensionSettings =
            Registry.LocalMachine.OpenSubKey(
                $"{policyRoot}\\{ExtensionSettingsSubKey}",
                writable: true);

        if (extensionSettings == null)
        {
            return;
        }

        extensionSettings.DeleteSubKey(
            ExtensionId,
            throwOnMissingSubKey: false);
    }
}
