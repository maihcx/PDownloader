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

namespace PDownloader.Models;

public partial class DownloadItemDto : ObservableObject
{
    public DownloadItemDto()
    {
        LanguageBase.LanguageChanged += LanguageBase_LanguageChanged;
    }

    private void LanguageBase_LanguageChanged(string language)
    {
        RefreshStatusText();
    }

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("startTime")]
    public DateTime StartTime { get; set; }

    [JsonPropertyName("endTime")]
    public DateTime EndTime { get; set; }

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
    public string Status
    {
        get; set
        {
            field = value;

            _ = Enum.TryParse(value.Trim(), out DownloadStatus status);
            StatusState = status;

            RefreshStatusText();
        }
    } = string.Empty;

    [ObservableProperty]
    private DownloadStatus _statusState;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [JsonPropertyName("speedFormatted")]
    public string SpeedFormatted { get; set; } = string.Empty;

    [JsonPropertyName("etaFormatted")]
    public string EtaFormatted { get; set; } = string.Empty;

    [JsonPropertyName("totalFormatted")]
    public string TotalFormatted { get; set; } = string.Empty;

    [JsonPropertyName("downloadedFormatted")]
    public string DownloadedFormatted { get; set; } = string.Empty;

    [JsonPropertyName("errorMessage")]
    public string ErrorMessage
    {
        get; set
        {
            field = value;
            RefreshStatusText();
        }
    } = string.Empty;

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }

    [JsonPropertyName("fileMergeMode")]
    public string FileMergeMode { get; set; } = "Balanced";

    [JsonPropertyName("canPause")]
    public bool CanPause { get; set; }

    [JsonPropertyName("canResume")]
    public bool CanResume { get; set; }

    public bool CanResumeOrOpenFile => CanResume || StatusState == DownloadStatus.Completed;

    [JsonPropertyName("md5Hash")]
    public string Md5Hash { get; set; } = string.Empty;

    [JsonPropertyName("sha1Hash")]
    public string Sha1Hash { get; set; } = string.Empty;

    [JsonPropertyName("sha256Hash")]
    public string Sha256Hash { get; set; } = string.Empty;

    private void RefreshStatusText()
    {
        if (StatusState == DownloadStatus.Error)
        {
            StatusText = LanguageBase.GetLangValue("download_status_error_title", ErrorMessage);
        }
        else if (StatusState == DownloadStatus.Retrying)
        {
            StatusText = LanguageBase.GetLangValue("download_status_retrying_title", ErrorMessage);
        }
        else
        {
            StatusText = LanguageBase.GetLangValue(
                StatusState switch
                {
                    DownloadStatus.Queued => "download_status_queued_title",
                    DownloadStatus.Connecting => "download_status_connecting_title",
                    DownloadStatus.Downloading => "download_status_downloading_title",
                    DownloadStatus.Paused => "download_status_paused_title",
                    DownloadStatus.Merging => "download_status_merging_title",
                    DownloadStatus.Completed => "download_status_completed_title",
                    _ => "?"
                }
            );
        }
    }
    public static DownloadItemDto FromContract(PDownloader.Contracts.Downloads.DownloadItemDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new DownloadItemDto
        {
            Id = dto.Id,
            Url = dto.Url,
            FileName = dto.FileName,
            StartTime = dto.StartTime,
            EndTime = dto.EndTime,
            SavePath = dto.SavePath,
            TotalBytes = dto.TotalBytes,
            DownloadedBytes = dto.DownloadedBytes,
            SpeedBps = dto.SpeedBps,
            Progress = dto.Progress,
            SpeedFormatted = dto.SpeedFormatted,
            EtaFormatted = dto.EtaFormatted,
            TotalFormatted = dto.TotalFormatted,
            DownloadedFormatted = dto.DownloadedFormatted,
            ErrorMessage = dto.ErrorMessage,
            IsActive = dto.IsActive,
            FileMergeMode = dto.FileMergeMode,
            CanPause = dto.CanPause,
            CanResume = dto.CanResume,
            Md5Hash = dto.Md5Hash,
            Sha1Hash = dto.Sha1Hash,
            Sha256Hash = dto.Sha256Hash,
            Status = dto.Status
        };
    }

}
