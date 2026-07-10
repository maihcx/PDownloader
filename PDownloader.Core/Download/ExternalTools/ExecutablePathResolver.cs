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

namespace PDownloader.Core.Download.ExternalTools;

internal static class ExecutablePathResolver
{
    public static string? Find(string executableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableName);

        foreach (string candidate in GetCandidates(executableName))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return FindOnPath(executableName + ".exe")
            ?? FindOnPath(executableName);
    }

    private static IEnumerable<string> GetCandidates(string executableName)
    {
        yield return Path.Combine(AppContext.BaseDirectory, executableName + ".exe");
        yield return Path.Combine(AppContext.BaseDirectory, executableName);
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PDownloader",
            executableName + ".exe");
    }

    private static string? FindOnPath(string fileName)
    {
        string[] paths = Environment.GetEnvironmentVariable("PATH")?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? Array.Empty<string>();

        foreach (string rawDirectory in paths)
        {
            string directory = rawDirectory.Trim('"');
            if (directory.Length == 0)
            {
                continue;
            }

            string fullPath = Path.Combine(directory, fileName);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return null;
    }
}
