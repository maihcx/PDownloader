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

namespace PDownloader.Services.DownloadServices;

public class DownloadConfigService
{
    private const string TempProbePrefix = ".pdownloader-write-test-";

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
            string? raw = UserDataStore.GetValue<string>(DownloadSettingsProtocol.StoreKey);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            DownloadSettingsDto? loaded = JsonSerializer.Deserialize<DownloadSettingsDto>(raw);
            if (loaded != null)
            {
                configs!.ApplyContract(loaded);
            }
        }
        catch (JsonException)
        {
            // Malformed download DTOs can use defaults; an IPC/storage failure must surface.
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

        configs.FileMergeMode = NormalizeFileMergeMode(configs.FileMergeMode);
    }

    public void Reload()
    {
        LoadSettings(DownloadConfigs);
        EnsureDefaults(DownloadConfigs);
    }

    public bool TrySave(out string errorMessage)
    {
        errorMessage = string.Empty;

        try
        {
            DownloadConfigs configs = DownloadConfigs
                ?? throw new InvalidOperationException("Download settings are unavailable.");

            configs.DefaultTempFolder = NormalizeAndValidateTempFolder(
                configs.DefaultTempFolder);
            configs.DefaultThreadCount = Math.Clamp(configs.DefaultThreadCount, 1, 32);
            configs.FileMergeMode = NormalizeFileMergeMode(configs.FileMergeMode);

            string raw = JsonSerializer.Serialize(configs.ToContract());
            UserDataStore.SetValue(DownloadSettingsProtocol.StoreKey, raw);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    public void Save()
    {
        if (!TrySave(out string errorMessage))
        {
            throw new IOException(errorMessage);
        }
    }

    private static string NormalizeFileMergeMode(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "highperformance" => "HighPerformance",
            "dataintegrity" => "DataIntegrity",
            _ => "Balanced"
        };
    }

    private static string NormalizeAndValidateTempFolder(string? folder)
    {
        string candidate = string.IsNullOrWhiteSpace(folder)
            ? GetDefaultTempFolder()
            : Environment.ExpandEnvironmentVariables(folder.Trim().Trim('"'));

        string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        if (File.Exists(fullPath))
        {
            throw new IOException("The temporary path points to a file instead of a folder.");
        }

        Directory.CreateDirectory(fullPath);

        string probePath = Path.Combine(
            fullPath,
            TempProbePrefix + Guid.NewGuid().ToString("N") + ".tmp");

        try
        {
            using (FileStream stream = new(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                stream.WriteByte(0);
                stream.Flush(flushToDisk: true);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(probePath))
                {
                    File.Delete(probePath);
                }
            }
            catch
            {
                // The write test already succeeded; cleanup is best effort.
            }
        }

        return fullPath;
    }
}
