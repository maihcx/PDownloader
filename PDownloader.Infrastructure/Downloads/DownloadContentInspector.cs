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

internal static class DownloadContentInspector
{
    private const int ProbeBytes = 4096;

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
        string prefix = GetTextPrefix(buffer, length);
        return prefix.StartsWith("<!doctype html", StringComparison.Ordinal)
            || prefix.StartsWith("<html", StringComparison.Ordinal)
            || prefix.StartsWith("<!doctype htm", StringComparison.Ordinal);
    }

    public static void EnsureDownloadedMediaFile(
        string path,
        ResolvedStream stream)
    {
        if (!File.Exists(path))
        {
            throw new InvalidDataException("The media stream has been resolved but was not downloaded.");
        }

        long length = new FileInfo(path).Length;
        if (length <= 0)
        {
            throw new InvalidDataException("The media stream has downloaded but contains no data.");
        }

        if (stream.FilesizeApprox > 1024 * 1024
            && length < Math.Max(64 * 1024, stream.FilesizeApprox / 100))
        {
            throw new InvalidDataException(
                $"Unusually small download stream " +
                $"({length} bytes, expected approximately {stream.FilesizeApprox} bytes). " +
                "The direct media URL may have expired or returned an error document.");
        }

        byte[] buffer = new byte[Math.Min(ProbeBytes, checked((int)Math.Min(length, ProbeBytes)))];
        using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            int read = input.Read(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                throw new InvalidDataException("The media stream does not contain readable data.");
            }

            if (LooksLikeTextResponse(buffer, read))
            {
                throw new InvalidDataException("The resolved URL returned a manifest or an error response instead of a media file.");
            }

            if (IsIsoBaseMediaExtension(stream.Ext)
                && !ContainsIsoBaseMediaBox(buffer, read))
            {
                throw new InvalidDataException(
                    $"The downloaded stream .{stream.Ext} is not a valid MP4/M4A file. " +
                    "This is most likely a Vimeo DASH/HLS manifest or a CDN response that has expired.");
            }
        }
    }

    private static bool LooksLikeTextResponse(byte[] buffer, int length)
    {
        string prefix = GetTextPrefix(buffer, length);
        if (string.IsNullOrEmpty(prefix))
        {
            return false;
        }

        return prefix.StartsWith("<!doctype html", StringComparison.Ordinal)
            || prefix.StartsWith("<html", StringComparison.Ordinal)
            || prefix.StartsWith("<?xml", StringComparison.Ordinal)
            || prefix.StartsWith("<mpd", StringComparison.Ordinal)
            || prefix.StartsWith("#extm3u", StringComparison.Ordinal)
            || prefix.StartsWith("{", StringComparison.Ordinal)
            || prefix.StartsWith("[", StringComparison.Ordinal)
            || prefix.StartsWith("access denied", StringComparison.Ordinal)
            || prefix.StartsWith("unauthorized", StringComparison.Ordinal)
            || prefix.StartsWith("forbidden", StringComparison.Ordinal);
    }

    private static string GetTextPrefix(byte[] buffer, int length)
    {
        if (length < 1)
        {
            return string.Empty;
        }

        int offset = GetBomOffset(buffer, length);
        int checkLength = Math.Min(length - offset, 256);
        if (checkLength <= 0)
        {
            return string.Empty;
        }

        return Encoding.UTF8
            .GetString(buffer, offset, checkLength)
            .TrimStart('\0', '\uFEFF', ' ', '\t', '\r', '\n')
            .ToLowerInvariant();
    }

    private static bool IsIsoBaseMediaExtension(string? extension)
    {
        string normalized = (extension ?? string.Empty).TrimStart('.').ToLowerInvariant();
        return normalized is "mp4" or "m4a" or "m4v" or "mov" or "3gp" or "3g2";
    }

    private static bool ContainsIsoBaseMediaBox(byte[] buffer, int length)
    {
        ReadOnlySpan<byte> data = buffer.AsSpan(0, length);
        return ContainsAscii(data, "ftyp")
            || ContainsAscii(data, "moov")
            || ContainsAscii(data, "moof");
    }

    private static bool ContainsAscii(ReadOnlySpan<byte> data, string value)
    {
        ReadOnlySpan<byte> needle = Encoding.ASCII.GetBytes(value);
        return data.IndexOf(needle) >= 0;
    }

    private static int GetBomOffset(byte[] buffer, int length)
    {
        if (length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
        {
            return 3;
        }

        if (length >= 2
            && ((buffer[0] == 0xFF && buffer[1] == 0xFE)
                || (buffer[0] == 0xFE && buffer[1] == 0xFF)))
        {
            return 2;
        }

        return 0;
    }
}
