// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace PDownloader.Contracts.Downloads;

public static class DownloadSource
{
    public static DownloadKind DetectKind(string? url, string? fileName = null)
    {
        if (IsMagnet(url)
            || HasTorrentExtension(fileName)
            || TryGetHttpPath(url, out string path) && HasTorrentExtension(path))
        {
            return DownloadKind.Torrent;
        }

        return DownloadKind.Http;
    }

    public static bool IsMagnet(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.TrimStart().StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase);

    public static string GetMagnetDisplayName(string? value)
    {
        if (!IsMagnet(value))
        {
            return string.Empty;
        }

        string query = value![((value?.IndexOf('?') ?? -1) + 1)..];
        foreach (string part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = part.IndexOf('=');
            if (separator <= 0
                || !part[..separator].Equals("dn", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                return Uri.UnescapeDataString(part[(separator + 1)..].Replace('+', ' ')).Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        return string.Empty;
    }

    private static bool TryGetHttpPath(string? value, out string path)
    {
        path = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        path = uri.AbsolutePath;
        return true;
    }

    private static bool HasTorrentExtension(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase);
}
