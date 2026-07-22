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

/// <summary>
/// Mirror of Core's DownloadItemDto — received via CFS "muxt-get-downloader-list"
/// and "muxt-download-progress" commands.
/// </summary>
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
            RefreshStatusText();
        }
    } = string.Empty;

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
    public string ErrorMessage { get; set; } = string.Empty;

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }

    [JsonPropertyName("md5Hash")]
    public string Md5Hash { get; set; } = string.Empty;

    [JsonPropertyName("sha1Hash")]
    public string Sha1Hash { get; set; } = string.Empty;

    [JsonPropertyName("sha256Hash")]
    public string Sha256Hash { get; set; } = string.Empty;

    private void RefreshStatusText()
    {
        if (Status == "Error")
        {
            StatusText = LanguageBase.GetLangValue("download_status_error_title", ErrorMessage);
        }
        else
        {
            StatusText = LanguageBase.GetLangValue(
                Status switch
                {
                    "Queued" => "download_status_queued_title",
                    "Connecting" => "download_status_connecting_title",
                    "Downloading" => "download_status_downloading_title",
                    "Paused" => "download_status_paused_title",
                    "Merging" => "download_status_merging_title",
                    "Completed" => "download_status_completed_title",
                    _ => "?"
                }
            );
        }
    }
}
