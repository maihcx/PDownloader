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

namespace PDownloader.Infrastructure.Downloads;

public static class DownloadPathUtilities
{
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

    public static string? GetHeader(
        Dictionary<string, string>? headers,
        string headerName)
    {
        return headers?
            .FirstOrDefault(pair => pair.Key.Equals(headerName, StringComparison.OrdinalIgnoreCase))
            .Value;
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
}
