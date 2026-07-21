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

using System.Text.Json.Serialization;

namespace PDownloader.Runner.Models;

/// <summary>
/// Mirror of Core's DownloadItemDto — received via CFS "muxt-get-downloader-list"
/// and "muxt-download-progress" commands.
/// </summary>
public class DownloadItemDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("savePath")]
    public string SavePath { get; set; } = string.Empty;

    [JsonPropertyName("totalBytes")]
    public long TotalBytes { get; set; }

    [JsonPropertyName("downloadedBytes")]
    public long DownloadedBytes { get; set; }

    [JsonPropertyName("speedBps")]
    public double SpeedBps { get; set; }

    [JsonPropertyName("progress")]
    public double Progress { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("statusText")]
    public string StatusText { get; set; } = string.Empty;

    [JsonPropertyName("speedFormatted")]
    public string SpeedFormatted { get; set; } = string.Empty;

    [JsonPropertyName("etaFormatted")]
    public string EtaFormatted { get; set; } = string.Empty;

    [JsonPropertyName("totalFormatted")]
    public string TotalFormatted { get; set; } = string.Empty;

    [JsonPropertyName("downloadedFormatted")]
    public string DownloadedFormatted { get; set; } = string.Empty;

    [JsonPropertyName("errorMessage")]
    public string ErrorMessage { get; set; } = string.Empty;

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }

    private string _progressVisualizationMode = "None";
    private string _progressVisualizationStage = string.Empty;
    private List<DownloadThreadProgressDto> _threadProgress = new();

    [JsonPropertyName("progressVisualizationMode")]
    public string ProgressVisualizationMode
    {
        get => _progressVisualizationMode;
        set => _progressVisualizationMode = string.IsNullOrWhiteSpace(value)
            ? "None"
            : value;
    }

    [JsonPropertyName("progressVisualizationStage")]
    public string ProgressVisualizationStage
    {
        get => _progressVisualizationStage;
        set => _progressVisualizationStage = value ?? string.Empty;
    }

    [JsonPropertyName("threadProgress")]
    public List<DownloadThreadProgressDto> ThreadProgress
    {
        get => _threadProgress;
        set => _threadProgress = value ?? new List<DownloadThreadProgressDto>();
    }
}
