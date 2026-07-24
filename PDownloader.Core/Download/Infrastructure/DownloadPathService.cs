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

namespace PDownloader.Core.Download.Infrastructure;

internal sealed class DownloadPathService
{
    private const int CleanupAttempts = 5;
    private const int CleanupDelayMilliseconds = 100;

    public string GetTempDirectory(DownloadItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        string tempRoot = ResolveTempRoot(item);
        item.TempRootPath = tempRoot;
        return BuildPerDownloadTempDirectory(tempRoot, item.Id);
    }

    public string GetTempDirectory(string id) => BuildPerDownloadTempDirectory(
        GetConfiguredTempRoot(),
        id);

    public string GetOutputFolder(DownloadItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.SavePath))
        {
            return item.SavePath;
        }

        string? configuredFolder = CFSCommandHandler
            .DownloadConfigService
            .DownloadConfigs?
            .DefaultDownloadFolder;

        return string.IsNullOrWhiteSpace(configuredFolder)
            ? Helpers.GetDefaultFolder()
            : configuredFolder;
    }

    public string GetFinalPath(DownloadItem item)
    {
        string folder = GetOutputFolder(item);
        string name = string.IsNullOrWhiteSpace(item.FileName) ? "download" : item.FileName;
        return UniqueFilePath(folder, name);
    }

    public static string UniqueFilePath(string folder, string name)
    {
        string fullFolder = Path.GetFullPath(folder);
        Directory.CreateDirectory(fullFolder);

        string safeName = SanitizeFileName(name);
        string path = Path.Combine(fullFolder, safeName);
        if (!File.Exists(path))
        {
            return path;
        }

        string nameWithoutExtension = Path.GetFileNameWithoutExtension(safeName);
        string extension = Path.GetExtension(safeName);
        int counter = 1;

        do
        {
            path = Path.Combine(fullFolder, $"{nameWithoutExtension} ({counter}){extension}");
            counter++;
        }
        while (File.Exists(path));

        return path;
    }

    public static string GuessFileName(string url)
    {
        try
        {
            var uri = new Uri(url);
            string fileName = Path.GetFileName(uri.AbsolutePath);
            return string.IsNullOrWhiteSpace(fileName) ? "download" : fileName;
        }
        catch
        {
            return "download";
        }
    }

    public static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "download";
        }

        int lastSeparator = Math.Max(name.LastIndexOf('\\'), name.LastIndexOf('/'));
        if (lastSeparator >= 0 && lastSeparator < name.Length - 1)
        {
            name = name[(lastSeparator + 1)..];
        }
        else if (lastSeparator == name.Length - 1)
        {
            name = string.Empty;
        }

        foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalidCharacter, '_');
        }

        name = name.Trim('"', '\'', ' ').TrimEnd(' ', '.');
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
        {
            return "download";
        }

        string baseName = Path.GetFileNameWithoutExtension(name).TrimEnd(' ', '.');
        if (IsReservedWindowsDeviceName(baseName))
        {
            name = "_" + name;
        }

        return name;
    }

    private static bool IsReservedWindowsDeviceName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return name.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || name.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || name.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || name.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || IsNumberedDeviceName(name, "COM")
            || IsNumberedDeviceName(name, "LPT");
    }

    private static bool IsNumberedDeviceName(string name, string prefix)
    {
        return name.Length == prefix.Length + 1
            && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && name[^1] is >= '1' and <= '9';
    }

    public static string? GetHeader(
        Dictionary<string, string>? headers,
        string headerName)
    {
        return headers?
            .FirstOrDefault(pair => pair.Key.Equals(headerName, StringComparison.OrdinalIgnoreCase))
            .Value;
    }

    public static void CleanupTemp(string tempDirectory)
    {
        for (int attempt = 1; attempt <= CleanupAttempts; attempt++)
        {
            try
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }

                return;
            }
            catch (IOException) when (attempt < CleanupAttempts)
            {
                Thread.Sleep(CleanupDelayMilliseconds);
            }
            catch (UnauthorizedAccessException) when (attempt < CleanupAttempts)
            {
                Thread.Sleep(CleanupDelayMilliseconds);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Path] can not delete the temp folder '{tempDirectory}': {ex.Message}");
                return;
            }
        }
    }

    public static void DeleteTempFiles(DownloadItem item)
    {
        var pathService = new DownloadPathService();
        string tempDirectory = pathService.GetTempDirectory(item);
        MergeRecoveryManifest? pendingMerge = MergeRecoveryStore.TryLoad(tempDirectory);

        if (pendingMerge != null)
        {
            TryDeleteFile(MergeRecoveryStore.GetPartialOutputPath(pendingMerge));
        }

        CleanupTemp(tempDirectory);

        try
        {
            string? configuredFolder = CFSCommandHandler
                .DownloadConfigService
                .DownloadConfigs?
                .DefaultDownloadFolder;
            string folder = !string.IsNullOrWhiteSpace(item.SavePath)
                ? item.SavePath
                : string.IsNullOrWhiteSpace(configuredFolder)
                    ? Helpers.GetDefaultFolder()
                    : configuredFolder;
            string name = SanitizeFileName(
                string.IsNullOrWhiteSpace(item.FileName) ? "download" : item.FileName);
            string mergingPath = Path.Combine(Path.GetFullPath(folder), name) + ".merging";

            TryDeleteFile(mergingPath);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private static string ResolveTempRoot(DownloadItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.TempRootPath))
        {
            return NormalizeTempRoot(item.TempRootPath);
        }

        string configuredRoot = GetConfiguredTempRoot();
        string legacyRoot = GetLegacyTempRoot();
        string configuredDirectory = BuildPerDownloadTempDirectory(configuredRoot, item.Id);
        string legacyDirectory = BuildPerDownloadTempDirectory(legacyRoot, item.Id);

        if (ContainsDownloadData(configuredDirectory))
        {
            return configuredRoot;
        }

        if (!PathsEqual(configuredRoot, legacyRoot)
            && ContainsDownloadData(legacyDirectory))
        {
            return legacyRoot;
        }

        if (Directory.Exists(configuredDirectory))
        {
            return configuredRoot;
        }

        if (!PathsEqual(configuredRoot, legacyRoot)
            && Directory.Exists(legacyDirectory))
        {
            return legacyRoot;
        }

        return configuredRoot;
    }

    private static string GetConfiguredTempRoot()
    {
        string? configured = CFSCommandHandler
            .DownloadConfigService
            .DownloadConfigs?
            .DefaultTempFolder;

        return NormalizeTempRoot(configured);
    }

    private static string GetLegacyTempRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SM SOFT",
        "PDownloader",
        "Temp");

    private static string NormalizeTempRoot(string? path)
    {
        string fallback = GetLegacyTempRoot();
        if (string.IsNullOrWhiteSpace(path))
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(fallback));
        }

        try
        {
            string expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(expanded));
        }
        catch
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(fallback));
        }
    }

    private static string BuildPerDownloadTempDirectory(string root, string id)
    {
        string safeId = SanitizeFileName(id);
        return Path.Combine(root, safeId);
    }

    private static bool ContainsDownloadData(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return false;
        }

        try
        {
            return Directory.EnumerateFileSystemEntries(directory).Any();
        }
        catch
        {
            return false;
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }
}
