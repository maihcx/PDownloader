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
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
//
// Copyright (C) Song Mai Software.

namespace PDownloader.Models;

/// <summary>
/// UI projection of the download wire contract. Serialization stays in
/// PDownloader.Contracts.Downloads.DownloadItemDto.
/// </summary>
public partial class DownloadItemViewModel : ObservableObject
{
    public DownloadItemViewModel()
    {
        LanguageBase.LanguageChanged += LanguageBase_LanguageChanged;
    }

    public string Id { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string SavePath { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public long DownloadedBytes { get; set; }
    public double SpeedBps { get; set; }
    public double Progress { get; set; }

    public string Status
    {
        get;
        set
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

    public string SpeedFormatted { get; set; } = string.Empty;
    public string EtaFormatted { get; set; } = string.Empty;
    public string TotalFormatted { get; set; } = string.Empty;
    public string DownloadedFormatted { get; set; } = string.Empty;

    public string ErrorMessage
    {
        get;
        set
        {
            field = value;
            RefreshStatusText();
        }
    } = string.Empty;

    public bool IsActive { get; set; }
    public string FileMergeMode { get; set; } = "Balanced";
    public bool CanPause { get; set; }
    public bool CanResume { get; set; }
    public bool CanResumeOrOpenFile => CanResume || StatusState == DownloadStatus.Completed;
    public string Md5Hash { get; set; } = string.Empty;
    public string Sha1Hash { get; set; } = string.Empty;
    public string Sha256Hash { get; set; } = string.Empty;

    public static DownloadItemViewModel FromContract(DownloadItemDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new DownloadItemViewModel
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
            FileMergeMode = dto.FileMergeMode.ToString(),
            CanPause = dto.CanPause,
            CanResume = dto.CanResume,
            Md5Hash = dto.Md5Hash,
            Sha1Hash = dto.Sha1Hash,
            Sha256Hash = dto.Sha256Hash,
            Status = dto.Status.ToString()
        };
    }

    private void LanguageBase_LanguageChanged(string language)
    {
        RefreshStatusText();
    }

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
                });
        }
    }
}
