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

namespace PDownloader.Core.Download.ExternalTools.YtDlp;

internal sealed class YtDlpCookieFileService
{
    public static YtDlpCookieFileService Instance { get; } = new();

    private const string YoutubeCookieDomain = ".youtube.com";
    private const string CookieExpiry = "2147483647";

    private YtDlpCookieFileService()
    {
    }

    public string? Create(string? cookieHeader)
    {
        if (string.IsNullOrWhiteSpace(cookieHeader))
        {
            return null;
        }

        var content = new StringBuilder();
        content.AppendLine("# Netscape HTTP Cookie File");

        foreach (string pair in cookieHeader.Split(
            ';',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            int separatorIndex = pair.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            string name = pair[..separatorIndex].Trim();
            string value = pair[(separatorIndex + 1)..].Trim();
            if (name.Length == 0)
            {
                continue;
            }

            content.Append(YoutubeCookieDomain).Append('\t')
                .Append("TRUE").Append('\t')
                .Append('/').Append('\t')
                .Append("TRUE").Append('\t')
                .Append(CookieExpiry).Append('\t')
                .Append(name).Append('\t')
                .Append(value).Append('\n');
        }

        string path = Path.Combine(
            Path.GetTempPath(),
            $"pdownloader_cookies_{Guid.NewGuid():N}.txt");
        File.WriteAllText(
            path,
            content.ToString(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    public void DeleteSafe(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best effort cleanup.
        }
    }
}
