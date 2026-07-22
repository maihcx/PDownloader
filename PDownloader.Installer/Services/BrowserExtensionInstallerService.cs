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

    private const string ChromiumExtensionId = ExtensionId;
    private const string GeckoExtensionId = FirefoxExtensionId;

    private const string ExtensionSettingsSubKey = "ExtensionSettings";

    private const string ChromiumUpdateUri =
        "https://raw.githubusercontent.com/maihcx/PDownloader/main/BrowserExtension/update.xml";

    private const string GeckoExtensionSettingsValue = "ExtensionSettings";

    private const string GeckoInstallUrl =
        "https://raw.githubusercontent.com/maihcx/PDownloader/main/BrowserExtension/PDownloader.xpi";

    private enum BrowserEngine
    {
        Chromium,
        Gecko,
    }

    private static readonly (string DisplayName, BrowserEngine Engine, string PolicyRoot)[] SupportedBrowsers =
    {
        ("Google Chrome",   BrowserEngine.Chromium, @"SOFTWARE\Policies\Google\Chrome"),
        ("Microsoft Edge",  BrowserEngine.Chromium, @"SOFTWARE\Policies\Microsoft\Edge"),
        ("Brave",           BrowserEngine.Chromium, @"SOFTWARE\Policies\BraveSoftware\Brave"),
        ("Cốc Cốc",         BrowserEngine.Chromium, @"SOFTWARE\Policies\CocCoc\CocCoc"),

        ("Mozilla Firefox", BrowserEngine.Gecko,    @"SOFTWARE\Policies\Mozilla\Firefox"),
    };

    public static void InstallForAllBrowsers(string installDir)
    {
        _ = installDir;

        foreach ((string _, BrowserEngine engine, string policyRoot) in SupportedBrowsers)
        {
            try
            {
                switch (engine)
                {
                    case BrowserEngine.Chromium when IsValidChromiumExtensionId():
                        RegisterChromiumExtensionPolicy(policyRoot, ChromiumUpdateUri);
                        break;

                    case BrowserEngine.Gecko when !string.IsNullOrWhiteSpace(GeckoExtensionId):
                        RegisterGeckoExtensionPolicy(policyRoot);
                        break;
                }
            }
            catch
            {
                // A browser may not support the expected enterprise-policy
                // surface or an existing third-party policy may be malformed.
                // Extension installation must never abort the main installer.
            }
        }
    }

    public static void UninstallForAllBrowsers()
    {
        foreach ((string _, BrowserEngine engine, string policyRoot) in SupportedBrowsers)
        {
            try
            {
                switch (engine)
                {
                    case BrowserEngine.Chromium when IsValidChromiumExtensionId():
                        RemoveChromiumExtensionPolicy(policyRoot);
                        break;

                    case BrowserEngine.Gecko when !string.IsNullOrWhiteSpace(GeckoExtensionId):
                        RemoveGeckoExtensionPolicy(policyRoot);
                        break;
                }
            }
            catch
            {
                // Best-effort cleanup. Do not make uninstall fail because a
                // browser policy key is inaccessible or was changed externally.
            }
        }
    }

    private static bool IsValidChromiumExtensionId()
    {
        return !string.IsNullOrWhiteSpace(ChromiumExtensionId)
            && !ChromiumExtensionId.StartsWith("REPLACE_", StringComparison.Ordinal);
    }

    private static void RegisterChromiumExtensionPolicy(
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
                ChromiumExtensionId,
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

    private static void RegisterGeckoExtensionPolicy(string policyRoot)
    {
        using RegistryKey? browserPolicy =
            Registry.LocalMachine.CreateSubKey(
                policyRoot,
                writable: true);

        if (browserPolicy == null)
        {
            return;
        }

        JsonObject extensionSettings =
            ReadGeckoExtensionSettings(browserPolicy);

        extensionSettings[GeckoExtensionId] = new JsonObject
        {
            ["installation_mode"] = "force_installed",
            ["install_url"] = GeckoInstallUrl,
            ["updates_disabled"] = false,
        };

        WriteGeckoExtensionSettings(
            browserPolicy,
            extensionSettings);
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

        if (!extensionSettings.Remove(GeckoExtensionId))
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
