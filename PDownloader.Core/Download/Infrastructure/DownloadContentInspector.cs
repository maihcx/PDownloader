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

internal static class DownloadContentInspector
{
    public static bool IsHtmlContentType(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            return false;
        }

        string normalized = mediaType.Trim().ToLowerInvariant();
        return normalized is "text/html" or "application/xhtml+xml" or "text/xhtml";
    }

    public static bool LooksLikeHtml(byte[] buffer, int length)
    {
        if (length < 5)
        {
            return false;
        }

        int offset = GetBomOffset(buffer, length);
        int checkLength = Math.Min(length - offset, 100);
        if (checkLength < 5)
        {
            return false;
        }

        string prefix = Encoding.UTF8
            .GetString(buffer, offset, checkLength)
            .TrimStart()
            .ToLowerInvariant();

        return prefix.StartsWith("<!doctype html", StringComparison.Ordinal)
            || prefix.StartsWith("<html", StringComparison.Ordinal)
            || prefix.StartsWith("<!doctype htm", StringComparison.Ordinal);
    }

    private static int GetBomOffset(byte[] buffer, int length)
    {
        if (length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
        {
            return 3;
        }

        if (length >= 2 &&
            ((buffer[0] == 0xFF && buffer[1] == 0xFE) ||
             (buffer[0] == 0xFE && buffer[1] == 0xFF)))
        {
            return 2;
        }

        return 0;
    }
}
