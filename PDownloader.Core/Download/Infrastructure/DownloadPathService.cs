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

    public string GetTempDirectory(string id) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SM SOFT",
        "PDownloader",
        "Temp",
        id);

    public string GetOutputFolder(DownloadItem item)
    {
        return string.IsNullOrWhiteSpace(item.SavePath)
            ? CFSCommandHandler.DownloadConfigService.DownloadConfigs?.DefaultDownloadFolder
                ?? Helpers.GetDefaultFolder()
            : item.SavePath;
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

        // Always collapse user-provided values to a leaf filename. This blocks
        // both Windows and URL-style traversal regardless of the current OS.
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

        // Windows silently normalizes trailing spaces/dots and treats dot-only
        // path segments specially, so remove them before combining paths.
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
                Debug.WriteLine($"[Path] Không thể xóa thư mục temp '{tempDirectory}': {ex.Message}");
                return;
            }
        }
    }

    public static void DeleteTempFiles(string id, string? savePath, string? fileName)
    {
        var pathService = new DownloadPathService();
        CleanupTemp(pathService.GetTempDirectory(id));

        try
        {
            string folder = string.IsNullOrWhiteSpace(savePath)
                ? CFSCommandHandler.DownloadConfigService.DownloadConfigs?.DefaultDownloadFolder
                    ?? Helpers.GetDefaultFolder()
                : savePath;
            string name = SanitizeFileName(
                string.IsNullOrWhiteSpace(fileName) ? "download" : fileName);
            string mergingPath = Path.Combine(Path.GetFullPath(folder), name) + ".merging";

            if (File.Exists(mergingPath))
            {
                File.Delete(mergingPath);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }
}
