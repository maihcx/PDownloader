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

/// <summary>
/// Represents one item for regular downloads and one expandable torrent cluster
/// for every set of files sharing an info hash.
/// </summary>
public partial class DownloadGroupViewModel : ObservableObject
{
    public DownloadGroupViewModel(string key, bool isTorrentGroup)
    {
        Key = key;
        IsTorrentGroup = isTorrentGroup;
        LanguageBase.LanguageChanged += LanguageBase_LanguageChanged;
    }

    public string Key { get; }

    public bool IsTorrentGroup { get; }

    public ObservableCollection<DownloadItemViewModel> Items { get; } = [];

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _itemCountText = string.Empty;
    [ObservableProperty] private DateTime _startTime;
    [ObservableProperty] private DateTime _endTime;
    [ObservableProperty] private long _totalBytes;
    [ObservableProperty] private long _downloadedBytes;
    [ObservableProperty] private double _speedBps;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private DownloadStatus _statusState;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _totalFormatted = "0 B";
    [ObservableProperty] private string _downloadedFormatted = "0 B";
    [ObservableProperty] private string _speedFormatted = "0 B/s";
    [ObservableProperty] private bool _canPause;
    [ObservableProperty] private bool _canResume;
    [ObservableProperty] private bool _canRetry;
    [ObservableProperty] private bool _canCancel;

    public static string GetKey(DownloadItemViewModel item) =>
        item.IsTorrent && !string.IsNullOrWhiteSpace(item.TorrentInfoHash)
            ? $"torrent:{item.TorrentInfoHash}"
            : $"download:{item.Id}";

    public static bool IsTorrentKey(string key) =>
        key.StartsWith("torrent:", StringComparison.Ordinal);

    public void Upsert(DownloadItemViewModel item)
    {
        DownloadItemViewModel? existing = Items.FirstOrDefault(candidate => candidate.Id == item.Id);
        if (existing is null)
        {
            Items.Add(item);
        }
        else
        {
            Items[Items.IndexOf(existing)] = item;
        }

        Refresh();
    }

    public void Remove(string downloadId)
    {
        DownloadItemViewModel? item = Items.FirstOrDefault(candidate => candidate.Id == downloadId);
        if (item is not null)
        {
            Items.Remove(item);
            Refresh();
        }
    }

    public bool ContainsKeyword(string keyword) =>
        Contains(Title, keyword)
        || Contains(Subtitle, keyword)
        || Items.Any(item =>
            Contains(item.FileName, keyword)
            || Contains(item.TorrentRelativePath, keyword)
            || Contains(item.Url, keyword)
            || Contains(item.StatusText, keyword)
            || Contains(item.ErrorMessage, keyword)
            || Contains(item.SavePath, keyword));

    private void Refresh()
    {
        if (Items.Count == 0)
        {
            return;
        }

        DownloadItemViewModel first = Items[0];
        Title = IsTorrentGroup && !string.IsNullOrWhiteSpace(first.TorrentName)
            ? first.TorrentName
            : first.FileName;
        Subtitle = IsTorrentGroup
            ? first.TorrentInfoHash
            : first.Url;
        ItemCountText = LanguageBase.GetLangValue("task_num_title", Items.Count);
        StartTime = Items.Min(item => item.StartTime);
        EndTime = Items.Max(item => item.EndTime);
        TotalBytes = Items.Sum(item => Math.Max(0, item.TotalBytes));
        DownloadedBytes = Items.Sum(item => Math.Max(0, item.DownloadedBytes));
        SpeedBps = Items.Sum(item => Math.Max(0, item.SpeedBps));
        Progress = TotalBytes > 0
            ? Math.Clamp((double)DownloadedBytes / TotalBytes * 100, 0, 100)
            : Items.Average(item => item.Progress);
        StatusState = ResolveStatus();
        Status = StatusState.ToString();
        StatusText = ResolveStatusText(StatusState);
        TotalFormatted = FormatBytes(TotalBytes);
        DownloadedFormatted = FormatBytes(DownloadedBytes);
        SpeedFormatted = $"{FormatBytes((long)SpeedBps)}/s";
        CanPause = Items.Any(item => item.CanPause);
        CanResume = Items.Any(item => item.CanResume);
        CanRetry = Items.Any(item => item.StatusState == DownloadStatus.Error);
        CanCancel = Items.Any(item => item.StatusState is not DownloadStatus.Completed
            and not DownloadStatus.Cancelled);
    }

    private DownloadStatus ResolveStatus()
    {
        if (Items.All(item => item.StatusState == DownloadStatus.Completed))
        {
            return DownloadStatus.Completed;
        }

        DownloadStatus[] priority =
        [
            DownloadStatus.Error,
            DownloadStatus.Retrying,
            DownloadStatus.Merging,
            DownloadStatus.Downloading,
            DownloadStatus.Connecting,
            DownloadStatus.Paused,
            DownloadStatus.Queued
        ];
        return priority.FirstOrDefault(status => Items.Any(item => item.StatusState == status));
    }

    private string ResolveStatusText(DownloadStatus status)
    {
        string key = status switch
        {
            DownloadStatus.Queued => "download_status_queued_title",
            DownloadStatus.Connecting => "download_status_connecting_title",
            DownloadStatus.Downloading => "download_status_downloading_title",
            DownloadStatus.Paused => "download_status_paused_title",
            DownloadStatus.Merging => "download_status_merging_title",
            DownloadStatus.Completed => "download_status_completed_title",
            DownloadStatus.Retrying => "download_status_retrying_title",
            DownloadStatus.Error => "download_status_error_title",
            _ => "download_status_queued_title"
        };
        string error = Items.FirstOrDefault(item => item.StatusState == status)?.ErrorMessage
            ?? string.Empty;
        return status is DownloadStatus.Error or DownloadStatus.Retrying
            ? LanguageBase.GetLangValue(key, error)
            : LanguageBase.GetLangValue(key);
    }

    private void LanguageBase_LanguageChanged(string language) => Refresh();

    private static bool Contains(string? value, string keyword) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}
