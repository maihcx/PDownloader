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
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PDownloader.Installer.Services;

public static class BrowserExtensionInstallerService
{
    public const string ExtensionId = "nliblbkhgljcpdboininiepogjaegien";
    public const string FirefoxExtensionId = "pdownloader@maisoft.io.vn";

    private const string ExtensionSettingsSubKey = "ExtensionSettings";

    private const string UpdateURI =
        "https://raw.githubusercontent.com/maihcx/PDownloader/main/BrowserExtension/update.xml";

    private const string FirefoxPolicyRoot =
        @"SOFTWARE\Policies\Mozilla\Firefox";

    private const string FirefoxExtensionSettingsValue =
        "ExtensionSettings";

    private const string FirefoxInstallUrl =
        "https://raw.githubusercontent.com/maihcx/PDownloader/main/BrowserExtension/PDownloader.xpi";

    private static readonly (string DisplayName, string PolicyRoot)[] SupportedBrowsers =
    {
        ("Google Chrome",   @"SOFTWARE\Policies\Google\Chrome"),
        ("Microsoft Edge",  @"SOFTWARE\Policies\Microsoft\Edge"),
        ("Brave",           @"SOFTWARE\Policies\BraveSoftware\Brave"),
        ("Cốc Cốc",         @"SOFTWARE\Policies\CocCoc\CocCoc"),
    };

    public static void InstallForAllBrowsers(string installDir)
    {
        _ = installDir;

        if (!string.IsNullOrWhiteSpace(ExtensionId) &&
            !ExtensionId.StartsWith("REPLACE_", StringComparison.Ordinal))
        {
            foreach ((string _, string policyRoot) in SupportedBrowsers)
            {
                try
                {
                    RegisterExtensionPolicy(policyRoot, UpdateURI);
                }
                catch
                {
                    // Ignore unsupported Chromium-based browser policy.
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(FirefoxExtensionId))
        {
            try
            {
                RegisterFirefoxExtensionPolicy();
            }
            catch
            {
                // Ignore unsupported Firefox policy or malformed pre-existing policy.
            }
        }
    }

    public static void UninstallForAllBrowsers()
    {
        if (!string.IsNullOrWhiteSpace(ExtensionId) &&
            !ExtensionId.StartsWith("REPLACE_", StringComparison.Ordinal))
        {
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

        if (!string.IsNullOrWhiteSpace(FirefoxExtensionId))
        {
            try
            {
                RemoveFirefoxExtensionPolicy();
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

    private static void RegisterFirefoxExtensionPolicy()
    {
        using RegistryKey? firefoxPolicy =
            Registry.LocalMachine.CreateSubKey(
                FirefoxPolicyRoot,
                writable: true);

        if (firefoxPolicy == null)
        {
            return;
        }

        JsonObject extensionSettings =
            ReadFirefoxExtensionSettings(firefoxPolicy);

        extensionSettings[FirefoxExtensionId] = new JsonObject
        {
            ["installation_mode"] = "force_installed",
            ["install_url"] = FirefoxInstallUrl,
            ["updates_disabled"] = false,
        };

        WriteFirefoxExtensionSettings(
            firefoxPolicy,
            extensionSettings);
    }

    private static void RemoveFirefoxExtensionPolicy()
    {
        using RegistryKey? firefoxPolicy =
            Registry.LocalMachine.OpenSubKey(
                FirefoxPolicyRoot,
                writable: true);

        if (firefoxPolicy == null)
        {
            return;
        }

        JsonObject extensionSettings =
            ReadFirefoxExtensionSettings(firefoxPolicy);

        if (!extensionSettings.Remove(FirefoxExtensionId))
        {
            return;
        }

        if (extensionSettings.Count == 0)
        {
            firefoxPolicy.DeleteValue(
                FirefoxExtensionSettingsValue,
                throwOnMissingValue: false);
            return;
        }

        WriteFirefoxExtensionSettings(
            firefoxPolicy,
            extensionSettings);
    }

    private static JsonObject ReadFirefoxExtensionSettings(
        RegistryKey firefoxPolicy)
    {
        object? rawValue =
            firefoxPolicy.GetValue(FirefoxExtensionSettingsValue);

        string? json = rawValue switch
        {
            string[] lines when lines.Length > 0 => string.Join("", lines),
            string value when !string.IsNullOrWhiteSpace(value) => value,
            null => null,
            _ => throw new InvalidDataException(
                $"Unsupported Firefox {FirefoxExtensionSettingsValue} registry value type."),
        };

        if (string.IsNullOrWhiteSpace(json))
        {
            return new JsonObject();
        }

        JsonNode? parsed = JsonNode.Parse(json);
        return parsed as JsonObject
            ?? throw new InvalidDataException(
                $"Firefox {FirefoxExtensionSettingsValue} policy is not a JSON object.");
    }

    private static void WriteFirefoxExtensionSettings(
        RegistryKey firefoxPolicy,
        JsonObject extensionSettings)
    {
        string json = extensionSettings.ToJsonString(
            new JsonSerializerOptions
            {
                WriteIndented = false,
            });

        firefoxPolicy.SetValue(
            FirefoxExtensionSettingsValue,
            new[] { json },
            RegistryValueKind.MultiString);
    }
}
