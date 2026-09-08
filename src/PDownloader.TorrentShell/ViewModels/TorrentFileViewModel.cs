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

public partial class TorrentFileViewModel : ObservableObject
{
    public TorrentFileViewModel(TorrentShellFileDto source)
    {
        Source = source;
        TotalText = FormatBytes(source.Length);
        DownloadedText = FormatBytes(0);
        StatusText = GetStatusText(DownloadStatus.Queued, string.Empty);
        TranslationSource.Instance.PropertyChanged += TranslationSource_PropertyChanged;
    }

    public TorrentShellFileDto Source { get; }
    public int Index => Source.Index;
    public string DownloadId => Source.DownloadId;
    public string FileName => Source.FileName;
    public string RelativePath => Source.RelativePath;
    public long Length => Source.Length;
    public string SizeText => FormatBytes(Source.Length);

    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    private DownloadStatus _status = DownloadStatus.Queued;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _speedText = "–";

    [ObservableProperty]
    private double _speedBps;

    [ObservableProperty]
    private string _etaText = "–";

    [ObservableProperty]
    private string _downloadedText = string.Empty;

    [ObservableProperty]
    private string _totalText = string.Empty;

    [ObservableProperty]
    private string _savePath = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _canPause;

    [ObservableProperty]
    private bool _canResume;

    public bool IsCompleted => Status == DownloadStatus.Completed;
    public bool HasError => Status == DownloadStatus.Error;
    public bool CanCancel => Status is not DownloadStatus.Completed
        and not DownloadStatus.Cancelled;

    partial void OnStatusChanged(DownloadStatus value)
    {
        StatusText = GetStatusText(value, ErrorMessage);
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(CanCancel));
    }

    partial void OnErrorMessageChanged(string value)
    {
        StatusText = GetStatusText(Status, value);
    }

    public void ApplyProgress(DownloadItemDto item)
    {
        if (!string.Equals(item.Id, DownloadId, StringComparison.Ordinal))
        {
            return;
        }

        Progress = item.Progress;
        Status = item.Status;
        SpeedText = item.SpeedFormatted;
        SpeedBps = item.SpeedBps;
        EtaText = item.EtaFormatted;
        DownloadedText = item.DownloadedFormatted;
        TotalText = item.TotalFormatted;
        SavePath = item.SavePath;
        ErrorMessage = item.ErrorMessage;
        CanPause = item.CanPause;
        CanResume = item.CanResume;
    }

    private void TranslationSource_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        StatusText = GetStatusText(Status, ErrorMessage);
    }

    private static string GetStatusText(DownloadStatus status, string error) =>
        status switch
        {
            DownloadStatus.Queued => LanguageBase.GetLangValue("download_status_queued_title"),
            DownloadStatus.Connecting => LanguageBase.GetLangValue("download_status_connecting_title"),
            DownloadStatus.Downloading => LanguageBase.GetLangValue("download_status_downloading_title"),
            DownloadStatus.Paused => LanguageBase.GetLangValue("download_status_paused_title"),
            DownloadStatus.Merging => LanguageBase.GetLangValue("download_status_merging_title"),
            DownloadStatus.Completed => LanguageBase.GetLangValue("download_status_completed_title"),
            DownloadStatus.Cancelled => LanguageBase.GetLangValue("download_status_cancelled_title"),
            DownloadStatus.Retrying => LanguageBase.GetLangValue("download_status_retrying_title", error),
            DownloadStatus.Error => LanguageBase.GetLangValue("download_status_error_title", error),
            _ => status.ToString()
        };

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{value:0} {units[unit]}"
            : $"{value:0.##} {units[unit]}";
    }
}
