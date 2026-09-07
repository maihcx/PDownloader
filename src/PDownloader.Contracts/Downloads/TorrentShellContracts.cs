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

/// <summary>A selected file and its live state inside one torrent download.</summary>
public sealed class TorrentFileProgressDto
{
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("relativePath")] public string RelativePath { get; set; } = string.Empty;
    [JsonPropertyName("fileName")] public string FileName { get; set; } = string.Empty;
    [JsonPropertyName("savePath")] public string SavePath { get; set; } = string.Empty;
    [JsonPropertyName("length")] public long Length { get; set; }
    [JsonPropertyName("downloadedBytes")] public long DownloadedBytes { get; set; }
    [JsonPropertyName("speedBps")] public double SpeedBps { get; set; }
    [JsonPropertyName("progress")] public double Progress { get; set; }
    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DownloadStatus Status { get; set; } = DownloadStatus.Queued;
    [JsonPropertyName("errorMessage")] public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>Initial state used by TorrentShell's selection screen.</summary>
public sealed class TorrentShellSessionView
{
    [JsonPropertyName("downloadId")] public string DownloadId { get; init; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("infoHash")] public string InfoHash { get; init; } = string.Empty;
    [JsonPropertyName("totalBytes")] public long TotalBytes { get; init; }
    [JsonPropertyName("saveTo")] public string SaveTo { get; init; } = string.Empty;
    [JsonPropertyName("destinationSubfolder")] public string DestinationSubfolder { get; init; } = string.Empty;
    [JsonPropertyName("isStarted")] public bool IsStarted { get; init; }
    [JsonPropertyName("categories")] public List<DownloadCategoryDto> Categories { get; init; } = [];
    [JsonPropertyName("selectedCategoryId")] public string SelectedCategoryId { get; init; } = string.Empty;
    [JsonPropertyName("files")] public List<TorrentFileProgressDto> Files { get; init; } = [];
}

public sealed class TorrentShellStartRequest
{
    [JsonPropertyName("selectedFileIndexes")] public List<int> SelectedFileIndexes { get; init; } = [];
    [JsonPropertyName("saveTo")] public string SaveTo { get; init; } = string.Empty;
    [JsonPropertyName("categoryId")] public string CategoryId { get; init; } = string.Empty;
    [JsonPropertyName("rememberPathForCategory")] public bool RememberPathForCategory { get; init; }
}

public static class TorrentShellLaunchProtocol
{
    public const string TokenArgument = "--token";
}
