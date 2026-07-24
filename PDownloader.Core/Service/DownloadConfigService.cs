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

using System.Reflection;

namespace PDownloader.Core.Services.DownloadServices;

public class DownloadConfigService
{
    private const string StoreKey = "pd-app-settings-v1";

    public DownloadConfigs? DownloadConfigs { get; private set; } = new DownloadConfigs();

    public DownloadConfigService()
    {
        LoadSettings(DownloadConfigs);
        EnsureDefaults(DownloadConfigs);
    }

    public static string GetDefaultTempFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SM SOFT",
        "PDownloader",
        "Temp");

    private void LoadSettings(DownloadConfigs? configs)
    {
        try
        {
            UserDataStore.Reload();
            string? raw = UserDataStore.GetValue<string>(StoreKey);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            DownloadConfigs? loaded = JsonSerializer.Deserialize<DownloadConfigs>(raw);
            if (loaded != null)
            {
                CopyProperties(loaded, configs!);
            }
        }
        catch
        {
            // Keep defaults when an old or malformed settings payload cannot be read.
        }
    }

    private static void EnsureDefaults(DownloadConfigs? configs)
    {
        if (configs == null)
        {
            return;
        }

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

    private static void CopyProperties(DownloadConfigs source, DownloadConfigs target)
    {
        foreach (PropertyInfo property in typeof(DownloadConfigs).GetProperties())
        {
            if (!property.CanRead || !property.CanWrite)
            {
                continue;
            }

            property.SetValue(target, property.GetValue(source));
        }
    }

    public void Reload()
    {
        LoadSettings(DownloadConfigs);
        EnsureDefaults(DownloadConfigs);
    }

    public FileMergeMode GetFileMergeMode() =>
        FileMergeModeParser.Parse(DownloadConfigs?.FileMergeMode);
}
