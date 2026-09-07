// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using MonoTorrent;

namespace PDownloader.Downloads.Torrents;

public sealed record TorrentPreparedFile(
    int Index,
    string RelativePath,
    string FileName,
    long Length);

public sealed class TorrentPreparation
{
    internal TorrentPreparation(byte[] metadata, Torrent torrent)
    {
        Metadata = metadata;
        Torrent = torrent;
        Name = torrent.Name;
        InfoHash = torrent.InfoHashes.V1OrV2.ToHex();
        Files = torrent.Files.Select((file, index) => (file, index))
            .Where(entry => entry.file is not TorrentFile torrentFile
                || (torrentFile.Attributes & TorrentFileAttributes.Padding) == 0)
            .Select(entry => new TorrentPreparedFile(
                entry.index,
                NormalizeRelativePath(entry.file.Path),
                Path.GetFileName(entry.file.Path),
                entry.file.Length))
            .ToArray();
    }

    internal byte[] Metadata { get; }
    internal Torrent Torrent { get; }
    public string Name { get; }
    public string InfoHash { get; }
    public IReadOnlyList<TorrentPreparedFile> Files { get; }
    public long TotalBytes => Files.Sum(file => file.Length);

    private static string NormalizeRelativePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');
}
