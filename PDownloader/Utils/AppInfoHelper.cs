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

namespace PDownloader.Utils;

public static class AppInfoHelper
{
    public static readonly string AppName = Assembly.GetExecutingAssembly().GetName().Name ?? string.Empty;
    public static string Author = "Song Mai Software";
    public static string SortAuthor = "SM SOFT";
    public static string AuthorCreated = "Created by SM SOFT";
    public static string AppDescription = "A fast, lightweight download manager for Windows.";
    public static string CopyRight = "© 2026 Song Mai Software";

    public static string GetAppPath()
    {
        string? appPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(appPath))
        {
            appPath = AppDomain.CurrentDomain.BaseDirectory;
        }
        else
        {
            appPath = Path.GetDirectoryName(appPath) ?? appPath;
        }

        return appPath.Replace("\\", "/");
    }

    public static string GetAppPackage()
    {
        string? exePath = Environment.ProcessPath;

        if (string.IsNullOrEmpty(exePath))
        {
            exePath = Assembly.GetEntryAssembly()?.Location;
        }

        if (string.IsNullOrEmpty(exePath))
        {
            return string.Empty;
        }

        return Path.GetFileName(exePath);
    }
}
