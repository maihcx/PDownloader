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

namespace PDownloader.Core.Services.DownloadServices;

public class DownloadConfigService
{
    public DownloadSettingsDto DownloadConfigs { get; private set; } = new();

    public DownloadConfigService()
    {
        Reload();
    }

    public static string GetDefaultTempFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SM SOFT",
        "PDownloader",
        "Temp");

    private static DownloadSettingsDto LoadSettings()
    {
        try
        {
            UserDataStore.Reload();
            string? raw = UserDataStore.GetValue<string>(DownloadSettingsProtocol.StoreKey);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                DownloadSettingsDto? loaded = JsonSerializer.Deserialize<DownloadSettingsDto>(raw);
                if (loaded != null)
                {
                    return loaded;
                }
            }
        }
        catch
        {
            // Keep defaults when an old or malformed settings payload cannot be read.
        }

        return new DownloadSettingsDto();
    }

    private static void EnsureDefaults(DownloadSettingsDto configs)
    {
        if (string.IsNullOrWhiteSpace(configs.DefaultTempFolder))
        {
            configs.DefaultTempFolder = GetDefaultTempFolder();
        }

        if (configs.DefaultThreadCount <= 0)
        {
            configs.DefaultThreadCount = 8;
        }

        configs.FileMergeMode = FileMergeModeParser
            .Parse(configs.FileMergeMode)
            .ToConfigValue();
    }

    public void Reload()
    {
        DownloadSettingsDto configs = LoadSettings();
        EnsureDefaults(configs);
        DownloadConfigs = configs;
    }

    public FileMergeMode GetFileMergeMode() =>
        FileMergeModeParser.Parse(DownloadConfigs.FileMergeMode);
}
