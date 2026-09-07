// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace PDownloader.Contracts.Downloads;

/// <summary>
/// Identifies the transfer engine used by a normal, single-file download item.
/// A torrent with several selected files is represented by several items which
/// share one torrent session in Core.
/// </summary>
public enum DownloadKind
{
    Http,
    Torrent
}
