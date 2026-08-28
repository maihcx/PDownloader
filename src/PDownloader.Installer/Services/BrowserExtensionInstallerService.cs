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

    private enum ChromiumExternalRegistrationState
    {
        Missing,
        MatchesInstaller,
        DiffersFromInstaller,
    }

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

    public static void InstallForBrowsers(
        string installDir,
        InstallScope installScope)
    {
        _ = installDir;

        foreach ((
            string _,
            string extensionRoot,
            string policyRoot) in SupportedChromiumBrowsers)
        {
            try
            {
                RemoveLegacyChromiumExtension(
                    extensionRoot,
                    policyRoot,
                    installScope);

                if (!IsValidChromiumExtensionId())
                {
                    continue;
                }

                ChromiumExternalRegistrationState registrationState =
                    GetChromiumExternalRegistrationState(
                        extensionRoot,
                        installScope);

                if (registrationState ==
                    ChromiumExternalRegistrationState.DiffersFromInstaller)
                {
                    // Replace only a stale or malformed registration. A matching
                    // registration is preserved across application updates.
                    RemoveChromiumExternalExtension(
                        extensionRoot,
                        ChromiumExtensionId,
                        installScope);
                    RegisterChromiumExternalExtension(
                        extensionRoot,
                        ChromiumUpdateUri,
                        installScope);
                }
                else if (registrationState ==
                             ChromiumExternalRegistrationState.Missing
                         && !IsChromiumExtensionPolicyRegistered(
                             policyRoot,
                             installScope))
                {
                    // Register only on the first installation. Updates preserve
                    // the existing browser extension and its user state.
                    RegisterChromiumExternalExtension(
                        extensionRoot,
                        ChromiumUpdateUri,
                        installScope);
                }
            }
            catch
            {
                // A browser may not support the expected registration surface,
                // or an existing third-party policy may be malformed.
                // Extension installation must never abort the main installer.
            }
        }

        RemoveLegacyGeckoExtensionPolicies(installScope);
    }

    public static void RemoveLegacyExtensionsForBrowsers(
        InstallScope installScope)
    {
        foreach ((
            string _,
            string extensionRoot,
            string policyRoot) in SupportedChromiumBrowsers)
        {
            try
            {
                RemoveLegacyChromiumExtension(
                    extensionRoot,
                    policyRoot,
                    installScope);
            }
            catch
            {
                // Best-effort migration cleanup. Never make an application
                // update fail because a browser registry key is inaccessible.
            }
        }

        RemoveLegacyGeckoExtensionPolicies(installScope);
    }

    public static void UninstallForBrowsers(InstallScope installScope)
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
                    RemoveChromiumExternalExtension(
                        extensionRoot,
                        ChromiumExtensionId,
                        installScope);
                    RemoveChromiumExtensionPolicy(
                        policyRoot,
                        ChromiumExtensionId,
                        installScope);
                }

                RemoveLegacyChromiumExtension(
                    extensionRoot,
                    policyRoot,
                    installScope);
            }
            catch
            {
                // Best-effort cleanup. Do not make uninstall fail because a
                // browser policy key is inaccessible or was changed externally.
            }
        }

        RemoveLegacyGeckoExtensionPolicies(installScope);
    }

    private static ChromiumExternalRegistrationState GetChromiumExternalRegistrationState(
        string extensionRoot,
        InstallScope installScope)
    {
        using RegistryKey registryBase = RegistryKey.OpenBaseKey(
            GetRegistryHive(installScope),
            GetExternalExtensionRegistryView(installScope));
        using RegistryKey? extension = registryBase.OpenSubKey(
            extensionRoot + "\\" + ChromiumExtensionId);

        if (extension == null)
        {
            return ChromiumExternalRegistrationState.Missing;
        }

        return extension.GetValue("update_url") is string updateUrl
            && string.Equals(
                updateUrl,
                ChromiumUpdateUri,
                StringComparison.Ordinal)
            ? ChromiumExternalRegistrationState.MatchesInstaller
            : ChromiumExternalRegistrationState.DiffersFromInstaller;
    }

    private static bool IsChromiumExtensionPolicyRegistered(
        string policyRoot,
        InstallScope installScope)
    {
        using RegistryKey registryBase = RegistryKey.OpenBaseKey(
            GetRegistryHive(installScope),
            RegistryView.Registry64);
        using RegistryKey? extension = registryBase.OpenSubKey(
            $"{policyRoot}\\{ExtensionSettingsSubKey}\\{ChromiumExtensionId}");

        return extension != null;
    }

    private static bool IsValidChromiumExtensionId()
    {
        return !string.IsNullOrWhiteSpace(ChromiumExtensionId)
            && !ChromiumExtensionId.StartsWith("REPLACE_", StringComparison.Ordinal);
    }

    private static void RegisterChromiumExternalExtension(
        string extensionRoot,
        string updateUrl,
        InstallScope installScope)
    {
        // Machine-wide Chromium registration uses the 32-bit view
        // (Wow6432Node). Chromium reads a per-user registration from the
        // current user's normal registry view.
        using RegistryKey registryBase = RegistryKey.OpenBaseKey(
            GetRegistryHive(installScope),
            GetExternalExtensionRegistryView(installScope));
        using RegistryKey? extension =
            registryBase.CreateSubKey(
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

    private static void RemoveChromiumExternalExtension(
        string extensionRoot,
        string extensionId,
        InstallScope installScope)
    {
        using RegistryKey registryBase = RegistryKey.OpenBaseKey(
            GetRegistryHive(installScope),
            GetExternalExtensionRegistryView(installScope));
        using RegistryKey? extensions = registryBase.OpenSubKey(
            extensionRoot,
            writable: true);

        extensions?.DeleteSubKey(
            extensionId,
            throwOnMissingSubKey: false);
    }

    private static void RemoveChromiumExtensionPolicy(
        string policyRoot,
        string extensionId,
        InstallScope installScope)
    {
        using RegistryKey registryBase = RegistryKey.OpenBaseKey(
            GetRegistryHive(installScope),
            RegistryView.Registry64);
        using RegistryKey? extensionSettings =
            registryBase.OpenSubKey(
                $"{policyRoot}\\{ExtensionSettingsSubKey}",
                writable: true);

        if (extensionSettings == null)
        {
            return;
        }

        extensionSettings.DeleteSubKey(
            extensionId,
            throwOnMissingSubKey: false);
    }

    private static void RemoveLegacyChromiumExtension(
        string extensionRoot,
        string policyRoot,
        InstallScope installScope)
    {
        RemoveChromiumExternalExtension(
            extensionRoot,
            LegacySelfHostedChromiumExtensionId,
            installScope);
        RemoveChromiumExtensionPolicy(
            policyRoot,
            LegacySelfHostedChromiumExtensionId,
            installScope);
    }

    private static void RemoveLegacyGeckoExtensionPolicies(
        InstallScope installScope)
    {
        foreach (string policyRoot in LegacyGeckoPolicyRoots)
        {
            try
            {
                // Cleanup only. Current installers never register, download, or
                // launch extensions for Firefox and other Gecko-based browsers.
                RemoveGeckoExtensionPolicy(policyRoot, installScope);
            }
            catch
            {
                // Preserve unrelated browser policies if their existing value
                // cannot be parsed or updated safely.
            }
        }
    }

    private static void RemoveGeckoExtensionPolicy(
        string policyRoot,
        InstallScope installScope)
    {
        using RegistryKey registryBase = RegistryKey.OpenBaseKey(
            GetRegistryHive(installScope),
            RegistryView.Registry64);
        using RegistryKey? browserPolicy =
            registryBase.OpenSubKey(
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

    private static RegistryHive GetRegistryHive(InstallScope installScope) =>
        installScope == InstallScope.AllUsers
            ? RegistryHive.LocalMachine
            : RegistryHive.CurrentUser;

    private static RegistryView GetExternalExtensionRegistryView(
        InstallScope installScope) =>
        installScope == InstallScope.AllUsers
            ? RegistryView.Registry32
            : RegistryView.Registry64;
}
