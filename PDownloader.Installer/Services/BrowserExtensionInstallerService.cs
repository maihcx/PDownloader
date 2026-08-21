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
    public const string ExtensionId = "kdbapmeegoljihpndnbfeockjjcoogbp";

    private const string ChromiumExtensionId = ExtensionId;
    private const string LegacyGeckoExtensionId = "pdownloader@maisoft.io.vn";

    private const string ExtensionSettingsSubKey = "ExtensionSettings";

    private const string ChromiumUpdateUri =
        "https://clients2.google.com/service/update2/crx";

    private const string GeckoExtensionSettingsValue = "ExtensionSettings";

    private const string LegacySelfHostedChromiumExtensionId =
        "nliblbkhgljcpdboininiepogjaegien";

    private static readonly (
        string DisplayName,
        string ExtensionRoot,
        string PolicyRoot)[] SupportedChromiumBrowsers =
    {
        (
            "Google Chrome",
            @"SOFTWARE\Google\Chrome\Extensions",
            @"SOFTWARE\Policies\Google\Chrome"),
        (
            "Microsoft Edge",
            @"SOFTWARE\Microsoft\Edge\Extensions",
            @"SOFTWARE\Policies\Microsoft\Edge"),
        (
            "Brave",
            @"SOFTWARE\BraveSoftware\Brave\Extensions",
            @"SOFTWARE\Policies\BraveSoftware\Brave"),
        (
            "Cốc Cốc",
            @"SOFTWARE\CocCoc\CocCoc\Extensions",
            @"SOFTWARE\Policies\CocCoc\CocCoc"),
    };

    private static readonly string[] LegacyGeckoPolicyRoots =
    {
        @"SOFTWARE\Policies\Mozilla\Firefox",
        @"SOFTWARE\Policies\Mozilla\Zen",
    };

    public static void InstallForAllBrowsers(string installDir)
    {
        _ = installDir;

        foreach ((
            string _,
            string extensionRoot,
            string policyRoot) in SupportedChromiumBrowsers)
        {
            try
            {
                if (!IsValidChromiumExtensionId())
                {
                    continue;
                }

                // Remove policies written by older PDownloader versions.
                // A policy-installed extension is managed by the browser and
                // does not show the normal third-party installation prompt.
                RemoveChromiumExtensionPolicy(policyRoot);
                RemoveLegacySelfHostedChromiumPolicy(policyRoot);

                // External registration is the consumer-facing flow used by
                // desktop applications that bundle a companion extension.
                // The browser asks the user to enable it on the next start.
                RegisterChromiumExternalExtension(
                    extensionRoot,
                    ChromiumUpdateUri);
            }
            catch
            {
                // A browser may not support the expected registration surface,
                // or an existing third-party policy may be malformed.
                // Extension installation must never abort the main installer.
            }
        }

        RemoveLegacyGeckoExtensionPolicies();
    }

    public static void UninstallForAllBrowsers()
    {
        foreach ((
            string _,
            string extensionRoot,
            string policyRoot) in SupportedChromiumBrowsers)
        {
            try
            {
                if (IsValidChromiumExtensionId())
                {
                    RemoveChromiumExternalExtension(extensionRoot);
                    RemoveChromiumExtensionPolicy(policyRoot);
                    RemoveLegacySelfHostedChromiumPolicy(policyRoot);
                }
            }
            catch
            {
                // Best-effort cleanup. Do not make uninstall fail because a
                // browser policy key is inaccessible or was changed externally.
            }
        }

        RemoveLegacyGeckoExtensionPolicies();
    }

    private static bool IsValidChromiumExtensionId()
    {
        return !string.IsNullOrWhiteSpace(ChromiumExtensionId)
            && !ChromiumExtensionId.StartsWith("REPLACE_", StringComparison.Ordinal);
    }

    private static void RegisterChromiumExternalExtension(
        string extensionRoot,
        string updateUrl)
    {
        // Chrome and Edge document external extension registration in the
        // 32-bit registry view. On 64-bit Windows this maps to Wow6432Node.
        using RegistryKey localMachine = RegistryKey.OpenBaseKey(
            RegistryHive.LocalMachine,
            RegistryView.Registry32);
        using RegistryKey? extension =
            localMachine.CreateSubKey(
                extensionRoot + "\\" + ChromiumExtensionId,
                writable: true);

        if (extension == null)
        {
            return;
        }

        extension.SetValue(
            "update_url",
            updateUrl,
            RegistryValueKind.String);
    }

    private static void RemoveChromiumExternalExtension(string extensionRoot)
    {
        using RegistryKey localMachine = RegistryKey.OpenBaseKey(
            RegistryHive.LocalMachine,
            RegistryView.Registry32);
        using RegistryKey? extensions = localMachine.OpenSubKey(
            extensionRoot,
            writable: true);

        extensions?.DeleteSubKey(
            ChromiumExtensionId,
            throwOnMissingSubKey: false);
    }

    private static void RemoveChromiumExtensionPolicy(string policyRoot)
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
            ChromiumExtensionId,
            throwOnMissingSubKey: false);
    }

    private static void RemoveLegacySelfHostedChromiumPolicy(string policyRoot)
    {
        using RegistryKey? extensionSettings =
            Registry.LocalMachine.OpenSubKey(
                $"{policyRoot}\\{ExtensionSettingsSubKey}",
                writable: true);

        extensionSettings?.DeleteSubKey(
            LegacySelfHostedChromiumExtensionId,
            throwOnMissingSubKey: false);
    }

    private static void RemoveLegacyGeckoExtensionPolicies()
    {
        foreach (string policyRoot in LegacyGeckoPolicyRoots)
        {
            try
            {
                // Cleanup only. Current installers never register, download, or
                // launch extensions for Firefox and other Gecko-based browsers.
                RemoveGeckoExtensionPolicy(policyRoot);
            }
            catch
            {
                // Preserve unrelated browser policies if their existing value
                // cannot be parsed or updated safely.
            }
        }
    }

    private static void RemoveGeckoExtensionPolicy(string policyRoot)
    {
        using RegistryKey? browserPolicy =
            Registry.LocalMachine.OpenSubKey(
                policyRoot,
                writable: true);

        if (browserPolicy == null)
        {
            return;
        }

        JsonObject extensionSettings =
            ReadGeckoExtensionSettings(browserPolicy);

        if (!extensionSettings.Remove(LegacyGeckoExtensionId))
        {
            return;
        }

        if (extensionSettings.Count == 0)
        {
            browserPolicy.DeleteValue(
                GeckoExtensionSettingsValue,
                throwOnMissingValue: false);
            return;
        }

        WriteGeckoExtensionSettings(
            browserPolicy,
            extensionSettings);
    }

    private static JsonObject ReadGeckoExtensionSettings(
        RegistryKey browserPolicy)
    {
        object? rawValue =
            browserPolicy.GetValue(GeckoExtensionSettingsValue);

        string? json = rawValue switch
        {
            string[] lines when lines.Length > 0 => string.Join("", lines),
            string value when !string.IsNullOrWhiteSpace(value) => value,
            null => null,
            _ => throw new InvalidDataException(
                $"Unsupported Gecko {GeckoExtensionSettingsValue} registry value type."),
        };

        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        JsonNode? parsed = JsonNode.Parse(json);
        return parsed as JsonObject
            ?? throw new InvalidDataException(
                $"Gecko {GeckoExtensionSettingsValue} policy is not a JSON object.");
    }

    private static void WriteGeckoExtensionSettings(
        RegistryKey browserPolicy,
        JsonObject extensionSettings)
    {
        string json = extensionSettings.ToJsonString(
            new JsonSerializerOptions
            {
                WriteIndented = false,
            });

        browserPolicy.SetValue(
            GeckoExtensionSettingsValue,
            new[] { json },
            RegistryValueKind.MultiString);
    }
}
