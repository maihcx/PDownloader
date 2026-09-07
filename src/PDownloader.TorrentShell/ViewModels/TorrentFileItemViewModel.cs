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

namespace PDownloader.TorrentShell.ViewModels;

public sealed class TorrentFileItemViewModel : ObservableObject
{
    private bool _isSelected = true;
    private long _downloadedBytes;
    private double _speedBps;
    private double _progress;
    private DownloadStatus _status;
    private string _errorMessage = string.Empty;

    public TorrentFileItemViewModel(TorrentFileProgressDto source)
    {
        Index = source.Index;
        RelativePath = source.RelativePath;
        FileName = source.FileName;
        SavePath = source.SavePath;
        Length = source.Length;
        Apply(source);
    }

    public int Index { get; }
    public string RelativePath { get; }
    public string FileName { get; }
    public string SavePath { get; private set; }
    public long Length { get; }
    public string SizeText => FormatBytes(Length);
    public string DownloadedText => $"{FormatBytes(DownloadedBytes)} / {SizeText}";
    public string SpeedText => SpeedBps <= 0 ? "–" : $"{FormatBytes((long)SpeedBps)}/s";
    public string ProgressText => $"{Progress:F0}%";
    public string StatusText => Status == DownloadStatus.Error
        ? LanguageBase.GetLangValue("download_status_error_title", ErrorMessage)
        : Status == DownloadStatus.Retrying
            ? LanguageBase.GetLangValue("download_status_retry_title", ErrorMessage)
        : LanguageBase.GetLangValue(Status switch
        {
            DownloadStatus.Queued => "download_status_queued_title",
            DownloadStatus.Connecting => "download_status_connecting_title",
            DownloadStatus.Downloading => "download_status_downloading_title",
            DownloadStatus.Paused => "download_status_paused_title",
            DownloadStatus.Completed => "download_status_completed_title",
            _ => "download_status_queued_title"
        });

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public long DownloadedBytes
    {
        get => _downloadedBytes;
        private set { if (SetProperty(ref _downloadedBytes, value))
            {
                OnPropertyChanged(nameof(DownloadedText));
            }
        }
    }
    public double SpeedBps
    {
        get => _speedBps;
        private set { if (SetProperty(ref _speedBps, value))
            {
                OnPropertyChanged(nameof(SpeedText));
            }
        }
    }
    public double Progress
    {
        get => _progress;
        private set
        {
            double normalized = double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;
            if (SetProperty(ref _progress, normalized))
            {
                OnPropertyChanged(nameof(ProgressText));
            }
        }
    }
    public DownloadStatus Status
    {
        get => _status;
        private set { if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }
    public string ErrorMessage
    {
        get => _errorMessage;
        private set { if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public void Apply(TorrentFileProgressDto source)
    {
        SavePath = source.SavePath;
        DownloadedBytes = source.DownloadedBytes;
        SpeedBps = source.SpeedBps;
        Progress = source.Progress;
        Status = source.Status;
        ErrorMessage = source.ErrorMessage;
    }

    public void RefreshLanguage() => OnPropertyChanged(nameof(StatusText));

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }

        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.##} {units[unit]}";
    }
}
