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

using System.Globalization;

namespace PDownloader.Installer.Utils;

public static class InstallerPreferencesStore
{
    private const string StorageKey = "InstallerPreferences";

    public static InstallerPreferences Load()
    {
        InstallerPreferences defaults = CreateDefaults();
        InstallerPreferences? stored =
            UserDataStore.GetValue<InstallerPreferences?>(StorageKey);

        if (stored is null)
        {
            return defaults;
        }

        InstallScope installScope = Enum.IsDefined(
            typeof(InstallScope),
            stored.InstallScope)
            ? stored.InstallScope
            : defaults.InstallScope;

        return stored with
        {
            InstallScope = installScope,
            Language = NormalizeLanguage(stored.Language),
        };
    }

    public static void Save(InstallerPreferences preferences)
    {
        InstallerPreferences normalized = preferences with
        {
            Language = NormalizeLanguage(preferences.Language),
        };

        UserDataStore.SetValue(StorageKey, normalized);

        // Keep the installer's startup option synchronized with the setting
        // that the installed application already uses.
        UserDataStore.SetValue("IsStartAtBoot", normalized.RunAtStartup);
    }

    public static string NormalizeLanguage(string? language)
    {
        if (!string.IsNullOrWhiteSpace(language))
        {
            try
            {
                string languageCode = new CultureInfo(language)
                    .TwoLetterISOLanguageName;

                if (LanguageBase.SupportedLanguages.Any(item =>
                        item.TwoLetterISOLanguageName.Equals(
                            languageCode,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    return languageCode;
                }
            }
            catch (CultureNotFoundException)
            {
                // Fall back to English when persisted or command-line data is
                // no longer supported by this installer build.
            }
        }

        return "en";
    }

    private static InstallerPreferences CreateDefaults() => new()
    {
        RunAtStartup = UserDataStore.GetValue<bool>("IsStartAtBoot", true),
    };
}
