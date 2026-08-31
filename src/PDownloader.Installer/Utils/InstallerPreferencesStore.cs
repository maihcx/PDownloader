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
using System.Text.Json;

namespace PDownloader.Installer.Utils;

public static class InstallerPreferencesStore
{
    private const string StorageKey = "InstallerPreferences";

    public static InstallerPreferences Load()
    {
        // Read the current file once under the shared lease. The app writes root
        // keys; InstallerPreferences is only a snapshot from a previous install.
        IReadOnlyDictionary<string, JsonElement> settings = UserDataStore.ReadSnapshot();
        InstallerPreferences defaults = new();
        InstallerPreferences stored = ReadValue(settings, StorageKey, defaults);

        InstallScope installScope = Enum.IsDefined(
            typeof(InstallScope),
            stored.InstallScope)
            ? stored.InstallScope
            : defaults.InstallScope;

        return stored with
        {
            InstallScope = installScope,
            Language = NormalizeLanguage(ReadValue(settings, "Language", stored.Language)),
            RunAtStartup = ReadValue(settings, "IsStartAtBoot", stored.RunAtStartup),
        };
    }

    public static void Save(InstallerPreferences preferences)
    {
        InstallerPreferences normalized = preferences with
        {
            Language = NormalizeLanguage(preferences.Language),
        };

        // Commit both related values in one direct-file transaction.
        UserDataStore.SetValues(new Dictionary<string, object?>
        {
            [StorageKey] = normalized,
            ["IsStartAtBoot"] = normalized.RunAtStartup,
        });
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

    private static T ReadValue<T>(
        IReadOnlyDictionary<string, JsonElement> settings,
        string key,
        T fallback)
    {
        if (!settings.TryGetValue(key, out JsonElement value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return fallback;
        }

        try
        {
            return value.Deserialize<T>() ?? fallback;
        }
        catch (JsonException)
        {
            // An invalid individual value may use its fallback. Errors reading
            // the file itself still propagate from ReadSnapshot; never erase it.
            return fallback;
        }
    }
}
