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

/// <summary>
/// Stable wire contract used between Core, Main and Runner. Keep this type UI-free.
/// </summary>
public sealed class DownloadItemDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
    [JsonPropertyName("fileName")] public string FileName { get; set; } = string.Empty;
    [JsonPropertyName("savePath")] public string SavePath { get; set; } = string.Empty;
    [JsonPropertyName("startTime")] public DateTime StartTime { get; set; }
    [JsonPropertyName("endTime")] public DateTime EndTime { get; set; }
    [JsonPropertyName("totalBytes")] public long TotalBytes { get; set; }
    [JsonPropertyName("downloadedBytes")] public long DownloadedBytes { get; set; }
    [JsonPropertyName("speedBps")] public double SpeedBps { get; set; }
    [JsonPropertyName("progress")] public double Progress { get; set; }
    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DownloadStatus Status { get; set; } = DownloadStatus.Queued;
    [JsonPropertyName("statusText")] public string StatusText { get; set; } = string.Empty;
    [JsonPropertyName("speedFormatted")] public string SpeedFormatted { get; set; } = string.Empty;
    [JsonPropertyName("etaFormatted")] public string EtaFormatted { get; set; } = string.Empty;
    [JsonPropertyName("totalFormatted")] public string TotalFormatted { get; set; } = string.Empty;
    [JsonPropertyName("downloadedFormatted")] public string DownloadedFormatted { get; set; } = string.Empty;
    [JsonPropertyName("errorMessage")] public string ErrorMessage { get; set; } = string.Empty;
    [JsonPropertyName("isActive")] public bool IsActive { get; set; }
    [JsonPropertyName("progressVisualizationMode")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DownloadProgressVisualizationMode ProgressVisualizationMode { get; set; } = DownloadProgressVisualizationMode.None;
    [JsonPropertyName("progressVisualizationStage")] public string ProgressVisualizationStage { get; set; } = string.Empty;
    [JsonPropertyName("threadProgress")] public List<DownloadThreadProgress> ThreadProgress { get; set; } = new();
    [JsonPropertyName("isMergeProgressActive")] public bool IsMergeProgressActive { get; set; }
    [JsonPropertyName("md5Hash")] public string Md5Hash { get; set; } = string.Empty;
    [JsonPropertyName("sha1Hash")] public string Sha1Hash { get; set; } = string.Empty;
    [JsonPropertyName("sha256Hash")] public string Sha256Hash { get; set; } = string.Empty;
    [JsonPropertyName("fileMergeMode")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FileMergeMode FileMergeMode { get; set; } = FileMergeMode.Balanced;
    [JsonPropertyName("canPause")] public bool CanPause { get; set; }
    [JsonPropertyName("canResume")] public bool CanResume { get; set; }
}
