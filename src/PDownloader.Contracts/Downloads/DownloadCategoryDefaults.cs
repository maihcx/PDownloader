// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// Copyright (C) Song Mai Software.

namespace PDownloader.Contracts.Downloads;

public static class DownloadCategoryDefaults
{
    public const string CompressedId = "compressed";
    public const string DocumentsId = "documents";
    public const string ProgramsId = "programs";
    public const string MusicId = "music";
    public const string VideosId = "videos";
    public const string PicturesId = "pictures";
    public const string TorrentsId = "torrents";
    public const string OtherId = "other";

    public static List<DownloadCategoryDto> Create(string downloadsRoot)
    {
        string root = string.IsNullOrWhiteSpace(downloadsRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads")
            : downloadsRoot;

        return
        [
            New(CompressedId, "Compressed", root, "Compressed",
                ".7z", ".bz2", ".cab", ".gz", ".iso", ".rar", ".tar", ".xz", ".zip"),
            New(DocumentsId, "Documents", root, "Documents",
                ".csv", ".doc", ".docx", ".epub", ".odt", ".ods", ".odp", ".pdf",
                ".ppt", ".pptx", ".rtf", ".txt", ".xls", ".xlsx"),
            New(ProgramsId, "Programs", root, "Programs",
                ".apk", ".appx", ".appxbundle", ".bat", ".cmd", ".deb", ".dmg", ".exe",
                ".msi", ".msix", ".msixbundle", ".pkg", ".ps1", ".rpm"),
            New(MusicId, "Music", root, "Music",
                ".aac", ".alac", ".flac", ".m4a", ".mid", ".midi", ".mp3", ".ogg",
                ".opus", ".wav", ".wma"),
            New(VideosId, "Videos", root, "Video",
                ".avi", ".flv", ".m4v", ".mkv", ".mov", ".mp4", ".mpeg", ".mpg",
                ".ts", ".webm", ".wmv"),
            New(PicturesId, "Pictures", root, "Pictures",
                ".avif", ".bmp", ".gif", ".heic", ".ico", ".jpeg", ".jpg", ".png",
                ".svg", ".tif", ".tiff", ".webp"),
            CreateTorrent(root),
            New(OtherId, "Other", root, string.Empty)
        ];
    }

    public static DownloadCategoryDto CreateTorrent(string downloadsRoot)
    {
        string root = string.IsNullOrWhiteSpace(downloadsRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads")
            : downloadsRoot;
        return New(TorrentsId, "Torrents", root, "Torrents");
    }

    /// <summary>
    /// Completes settings loaded from versions that predate torrent groups.
    /// The caller decides when settings are persisted; this method performs no migration write.
    /// </summary>
    public static void EnsureTorrentCategory(
        IList<DownloadCategoryDto> categories,
        string downloadsRoot)
    {
        DownloadCategoryDto? existing = categories.FirstOrDefault(category =>
            string.Equals(category.Id, TorrentsId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.IsEnabled = true;
            if (string.IsNullOrWhiteSpace(existing.FolderPath))
            {
                existing.FolderPath = CreateTorrent(downloadsRoot).FolderPath;
            }
            return;
        }

        DownloadCategoryDto torrent = CreateTorrent(downloadsRoot);
        int otherIndex = categories.ToList().FindIndex(category =>
            string.Equals(category.Id, OtherId, StringComparison.OrdinalIgnoreCase));
        categories.Insert(otherIndex >= 0 ? otherIndex : categories.Count, torrent);
    }

    private static DownloadCategoryDto New(
        string id,
        string name,
        string root,
        string folder,
        params string[] extensions) => new()
        {
            Id = id,
            Name = name,
            FolderPath = string.IsNullOrEmpty(folder) ? root : Path.Combine(root, folder),
            Extensions = [.. extensions],
            IsEnabled = true
        };
}
