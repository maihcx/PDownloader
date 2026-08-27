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

namespace PDownloader.Infrastructure.ExternalTools.YtDlp;

internal static class VimeoUrlNormalizer
{
    public static string Normalize(string url, string? referer)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            || !IsHttp(uri)
            || !IsVimeoHost(uri.Host))
        {
            return url;
        }

        string[] segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        string videoId;
        string? unlistedHash;

        if (uri.Host.Equals("player.vimeo.com", StringComparison.OrdinalIgnoreCase))
        {
            if (segments.Length < 2
                || !segments[0].Equals("video", StringComparison.OrdinalIgnoreCase)
                || !IsNumericId(segments[1]))
            {
                return url;
            }

            videoId = segments[1];
            unlistedHash = GetUnlistedHash(uri, segments, 2);
        }
        else
        {
            int videoIdIndex = segments.Length >= 2
                && segments[0].Equals("video", StringComparison.OrdinalIgnoreCase)
                    ? 1
                    : 0;

            if (segments.Length <= videoIdIndex
                || !IsNumericId(segments[videoIdIndex]))
            {
                return url;
            }

            videoId = segments[videoIdIndex];
            unlistedHash = GetUnlistedHash(uri, segments, videoIdIndex + 1);
        }

        return BuildPlayerUrl(videoId, unlistedHash);
    }

    private static string BuildPlayerUrl(string videoId, string? unlistedHash)
    {
        string playerUrl = $"https://player.vimeo.com/video/{videoId}";
        return string.IsNullOrWhiteSpace(unlistedHash)
            ? playerUrl
            : $"{playerUrl}?h={Uri.EscapeDataString(unlistedHash)}";
    }

    private static string? GetUnlistedHash(
        Uri uri,
        IReadOnlyList<string> pathSegments,
        int pathHashIndex)
    {
        if (pathSegments.Count > pathHashIndex
            && IsSafeHash(pathSegments[pathHashIndex]))
        {
            return pathSegments[pathHashIndex];
        }

        foreach (string pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            if (!Uri.UnescapeDataString(parts[0]).Equals("h", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string value = parts.Length > 1
                ? Uri.UnescapeDataString(parts[1])
                : string.Empty;
            return IsSafeHash(value) ? value : null;
        }

        return null;
    }

    private static bool IsHttp(Uri uri)
    {
        return uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVimeoHost(string host)
    {
        return host.Equals("vimeo.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".vimeo.com", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNumericId(string value)
    {
        return value.Length > 0 && value.All(char.IsDigit);
    }

    private static bool IsSafeHash(string value)
    {
        return value.Length > 0
            && value.All(character => char.IsLetterOrDigit(character)
                || character is '-' or '_');
    }
}