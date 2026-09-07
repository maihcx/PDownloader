// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace PDownloader.Contracts.Downloads;

public sealed class TorrentSelectionFileDto
{
    public int Index { get; init; }
    public string RelativePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public long Length { get; init; }
}

public sealed class TorrentSelectionSessionView
{
    public string Name { get; init; } = string.Empty;
    public string InfoHash { get; init; } = string.Empty;
    public long TotalBytes { get; init; }
    public List<TorrentSelectionFileDto> Files { get; init; } = [];
}

public sealed class TorrentSelectionResult
{
    public List<int> SelectedFileIndexes { get; init; } = [];
}

public static class TorrentSelectionLaunchProtocol
{
    public const string TokenArgument = "--token";
}
