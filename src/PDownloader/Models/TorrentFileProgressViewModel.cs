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

namespace PDownloader.Models;

public sealed class TorrentFileProgressViewModel : ObservableObject
{
    public int Index { get; init; }
    public string RelativePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string SavePath { get; init; } = string.Empty;
    public long Length { get; init; }
    public long DownloadedBytes { get; init; }
    public double SpeedBps { get; init; }
    public double Progress { get; init; }
    public DownloadStatus Status { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public string ProgressText => $"{Progress:F0}%";
    public string SizeText => FormatBytes(Length);
    public string DownloadedText => $"{FormatBytes(DownloadedBytes)} / {SizeText}";
    public string SpeedText => SpeedBps <= 0 ? "–" : $"{FormatBytes((long)SpeedBps)}/s";
    public string StatusText => Status == DownloadStatus.Error
        ? LanguageBase.GetLangValue("download_status_error_title", ErrorMessage)
        : LanguageBase.GetLangValue(Status switch
        {
            DownloadStatus.Queued => "download_status_queued_title",
            DownloadStatus.Connecting => "download_status_connecting_title",
            DownloadStatus.Downloading => "download_status_downloading_title",
            DownloadStatus.Paused => "download_status_paused_title",
            DownloadStatus.Completed => "download_status_completed_title",
            DownloadStatus.Retrying => "download_status_retrying_title",
            _ => "download_status_queued_title"
        });

    public static TorrentFileProgressViewModel FromContract(TorrentFileProgressDto dto) => new()
    {
        Index = dto.Index,
        RelativePath = dto.RelativePath,
        FileName = dto.FileName,
        SavePath = dto.SavePath,
        Length = dto.Length,
        DownloadedBytes = dto.DownloadedBytes,
        SpeedBps = dto.SpeedBps,
        Progress = dto.Progress,
        Status = dto.Status,
        ErrorMessage = dto.ErrorMessage
    };

    public void RefreshLanguage() => OnPropertyChanged(nameof(StatusText));

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }

        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.##} {units[unit]}";
    }
}
