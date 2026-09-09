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

namespace PDownloader.Contracts.Downloads;

public sealed class TorrentShellFileDto
{
    public int Index { get; init; }
    public string DownloadId { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public long Length { get; init; }
}

public sealed class TorrentShellSessionView
{
    public string Name { get; init; } = string.Empty;
    public string InfoHash { get; init; } = string.Empty;
    public string SaveTo { get; init; } = string.Empty;
    public string DestinationSubfolder { get; init; } = string.Empty;
    public bool IsLoading { get; init; }
    public string MetadataError { get; init; } = string.Empty;
    public long TotalBytes { get; init; }
    public bool HasStarted { get; init; }
    public List<TorrentShellFileDto> Files { get; init; } = [];
    public List<DownloadCategoryDto> Categories { get; init; } = [];
    public string SelectedCategoryId { get; init; } = string.Empty;
}

public sealed class TorrentShellStartRequest
{
    public List<int> SelectedFileIndexes { get; init; } = [];
    public string SaveTo { get; init; } = string.Empty;
    public string CategoryId { get; init; } = string.Empty;
    public bool RememberPathForCategory { get; init; }
}

public sealed class TorrentShellStartResult
{
    public bool Success { get; init; }
    public string Error { get; init; } = string.Empty;
}

public static class TorrentShellLaunchProtocol
{
    public const string TokenArgument = "--token";
}
