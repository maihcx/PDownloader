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

public enum DownloadGroupControlTransition
{
    None,
    Pausing,
    Resuming
}

/// <summary>
/// Stable UI projection for one torrent file. Progress snapshots can change
/// without replacing the ItemsControl item/container which displays the file.
/// </summary>
public partial class TorrentDownloadItemViewModel : ObservableObject
{
    [ObservableProperty]
    private DownloadItemViewModel _snapshot;

    public TorrentDownloadItemViewModel(DownloadItemViewModel snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot;
        _snapshot.PropertyChanged += Snapshot_PropertyChanged;
    }

    public string Id => Snapshot.Id;
    public string FileName => Snapshot.FileName;
    public string TorrentRelativePath => Snapshot.TorrentRelativePath;
    public string SavePath => Snapshot.SavePath;
    public string Url => Snapshot.Url;
    public string Status => Snapshot.Status;
    public DownloadStatus StatusState => Snapshot.StatusState;
    public string StatusText => Snapshot.StatusText;
    public double Progress => Snapshot.Progress;
    public long TotalBytes => Snapshot.TotalBytes;
    public long DownloadedBytes => Snapshot.DownloadedBytes;
    public double SpeedBps => Snapshot.SpeedBps;
    public string TotalFormatted => Snapshot.TotalFormatted;
    public string DownloadedFormatted => Snapshot.DownloadedFormatted;
    public string SpeedFormatted => Snapshot.SpeedFormatted;
    public bool IsActive => Snapshot.IsActive;
    public bool CanPause => Snapshot.CanPause;
    public bool CanResume => Snapshot.CanResume;
    public bool CanResumeOrOpenFile => Snapshot.CanResumeOrOpenFile;

    public void Update(DownloadItemViewModel snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Snapshot = snapshot;
    }

    partial void OnSnapshotChanging(DownloadItemViewModel value)
    {
        if (_snapshot is not null)
        {
            _snapshot.PropertyChanged -= Snapshot_PropertyChanged;
        }
    }

    partial void OnSnapshotChanged(DownloadItemViewModel value)
    {
        value.PropertyChanged += Snapshot_PropertyChanged;
        OnPropertyChanged(string.Empty);
    }

    private void Snapshot_PropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        OnPropertyChanged(string.Empty);
}

/// <summary>
/// Stable projection for a torrent and its child files. UI state such as the
/// CardExpander expansion is owned here instead of by a recycled container.
/// </summary>
public partial class DownloadGroupViewModel : ObservableObject
{
    private readonly HashSet<string> _pendingControlIds = new(StringComparer.Ordinal);

    public DownloadGroupViewModel(string key, bool isTorrentGroup)
    {
        Key = key;
        IsTorrentGroup = isTorrentGroup;
        LanguageBase.LanguageChanged += LanguageBase_LanguageChanged;
    }

    public string Key { get; }
    public bool IsTorrentGroup { get; }
    public ObservableCollection<TorrentDownloadItemViewModel> Items { get; } = [];

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private string _itemCountText = string.Empty;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private long _totalBytes;

    [ObservableProperty]
    private DateTime _startTime;

    [ObservableProperty]
    private DateTime _endTime;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status))]
    [NotifyPropertyChangedFor(nameof(CanResumeOrOpenFolder))]
    [NotifyPropertyChangedFor(nameof(IsResumeControlVisible))]
    [NotifyPropertyChangedFor(nameof(CanPauseGroup))]
    [NotifyPropertyChangedFor(nameof(CanResumeGroup))]
    private DownloadStatus _statusState;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _downloadedFormatted = string.Empty;

    [ObservableProperty]
    private string _totalFormatted = string.Empty;

    [ObservableProperty]
    private string _speedFormatted = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPauseControlVisible))]
    [NotifyPropertyChangedFor(nameof(IsResumeControlVisible))]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPauseGroup))]
    private bool _canPause;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanResumeOrOpenFolder))]
    [NotifyPropertyChangedFor(nameof(CanResumeGroup))]
    private bool _canResume;

    [ObservableProperty]
    private bool _canRetry;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPauseControlVisible))]
    [NotifyPropertyChangedFor(nameof(IsResumeControlVisible))]
    [NotifyPropertyChangedFor(nameof(CanPauseGroup))]
    [NotifyPropertyChangedFor(nameof(CanResumeGroup))]
    private DownloadGroupControlTransition _controlTransition;

    public bool HasItems => Items.Count > 0;
    public string Status => StatusState.ToString();
    public bool CanResumeOrOpenFolder =>
        CanResume || StatusState == DownloadStatus.Completed;
    public bool IsPauseControlVisible =>
        ControlTransition == DownloadGroupControlTransition.Pausing
        || (ControlTransition != DownloadGroupControlTransition.Resuming && IsActive);
    public bool IsResumeControlVisible =>
        ControlTransition == DownloadGroupControlTransition.Resuming
        || (ControlTransition != DownloadGroupControlTransition.Pausing
            && !IsActive
            && StatusState is not DownloadStatus.Error and not DownloadStatus.Retrying);
    public bool CanPauseGroup =>
        ControlTransition == DownloadGroupControlTransition.None
        && CanPause
        && StatusState != DownloadStatus.Connecting;
    public bool CanResumeGroup =>
        ControlTransition == DownloadGroupControlTransition.None
        && CanResumeOrOpenFolder;

    public IEnumerable<DownloadItemViewModel> Snapshots =>
        Items.Select(item => item.Snapshot);

    public static string GetKey(DownloadItemViewModel item) =>
        item.IsTorrent && !string.IsNullOrWhiteSpace(item.TorrentInfoHash)
            ? $"torrent:{item.TorrentInfoHash}"
            : $"download:{item.Id}";

    public static bool IsTorrentKey(string key) =>
        key.StartsWith("torrent:", StringComparison.Ordinal);

    public bool ContainsKeyword(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return true;
        }

        return Contains(Title, keyword)
            || Contains(Subtitle, keyword)
            || Items.Any(item => Contains(item.FileName, keyword)
                || Contains(item.TorrentRelativePath, keyword)
                || Contains(item.SavePath, keyword)
                || Contains(item.Url, keyword)
                || Contains(item.StatusText, keyword));
    }

    /// <returns>True when a child was added; false when an existing child was updated.</returns>
    public bool Upsert(DownloadItemViewModel snapshot)
    {
        TorrentDownloadItemViewModel? existing =
            Items.FirstOrDefault(item => item.Id == snapshot.Id);
        bool added = existing is null;
        if (existing is null)
        {
            Items.Add(new TorrentDownloadItemViewModel(snapshot));
        }
        else
        {
            existing.Update(snapshot);
        }

        RefreshAggregate();
        return added;
    }

    public bool Remove(string downloadId)
    {
        TorrentDownloadItemViewModel? item =
            Items.FirstOrDefault(candidate => candidate.Id == downloadId);
        if (item is null)
        {
            return false;
        }

        Items.Remove(item);
        RefreshAggregate();
        return true;
    }

    public bool Retain(IReadOnlySet<string> downloadIds)
    {
        bool changed = false;
        foreach (TorrentDownloadItemViewModel item in Items
                     .Where(item => !downloadIds.Contains(item.Id))
                     .ToArray())
        {
            Items.Remove(item);
            changed = true;
        }

        if (changed)
        {
            RefreshAggregate();
        }

        return changed;
    }

    public void BeginPause(IEnumerable<string> downloadIds) =>
        BeginControlTransition(DownloadGroupControlTransition.Pausing, downloadIds);

    public void BeginResume(IEnumerable<string> downloadIds) =>
        BeginControlTransition(DownloadGroupControlTransition.Resuming, downloadIds);

    private void BeginControlTransition(
        DownloadGroupControlTransition transition,
        IEnumerable<string> downloadIds)
    {
        _pendingControlIds.Clear();
        foreach (string id in downloadIds.Where(id => !string.IsNullOrWhiteSpace(id)))
        {
            _pendingControlIds.Add(id);
        }

        ControlTransition = _pendingControlIds.Count == 0
            ? DownloadGroupControlTransition.None
            : transition;
    }

    private void RefreshAggregate()
    {
        OnPropertyChanged(nameof(HasItems));
        if (Items.Count == 0)
        {
            Title = string.Empty;
            Subtitle = string.Empty;
            ItemCountText = string.Empty;
            Progress = 0;
            TotalBytes = 0;
            StartTime = default;
            EndTime = default;
            StatusState = DownloadStatus.Cancelled;
            StatusText = string.Empty;
            DownloadedFormatted = FormatBytes(0);
            TotalFormatted = FormatBytes(0);
            SpeedFormatted = FormatSpeed(0);
            IsActive = false;
            CanPause = false;
            CanResume = false;
            CanRetry = false;
            _pendingControlIds.Clear();
            ControlTransition = DownloadGroupControlTransition.None;
            return;
        }

        DownloadItemViewModel first = Items[0].Snapshot;
        Title = !string.IsNullOrWhiteSpace(first.TorrentName)
            ? first.TorrentName
            : first.FileName;
        Subtitle = first.TorrentInfoHash;
        ItemCountText = LanguageBase.GetLangValue(
            "page_torrents_file_count",
            Items.Count);

        TotalBytes = Items.Sum(item => Math.Max(0, item.TotalBytes));
        long downloadedBytes = Items.Sum(item => Math.Max(0, item.DownloadedBytes));
        Progress = TotalBytes > 0
            ? Math.Clamp(downloadedBytes * 100d / TotalBytes, 0d, 100d)
            : Items.Average(item => Math.Clamp(item.Progress, 0d, 100d));
        DownloadedFormatted = FormatBytes(downloadedBytes);
        TotalFormatted = FormatBytes(TotalBytes);
        SpeedFormatted = FormatSpeed(Items.Sum(item => Math.Max(0d, item.SpeedBps)));
        StartTime = Items.Min(item => item.Snapshot.StartTime);
        EndTime = Items.Max(item => item.Snapshot.EndTime);

        IsActive = Items.Any(item => item.IsActive);
        CanPause = Items.Any(item => item.CanPause);
        CanResume = Items.Any(item => item.CanResume);
        CanRetry = Items.Any(item => item.StatusState == DownloadStatus.Error);
        StatusState = GetAggregateStatus();
        RefreshControlTransition();
        RefreshStatusText();
    }

    private void RefreshControlTransition()
    {
        if (ControlTransition == DownloadGroupControlTransition.None)
        {
            return;
        }

        _pendingControlIds.RemoveWhere(id => Items.All(item => item.Id != id));
        bool isWaiting = ControlTransition switch
        {
            DownloadGroupControlTransition.Pausing => Items.Any(item =>
                _pendingControlIds.Contains(item.Id)
                && item.StatusState is not DownloadStatus.Paused
                    and not DownloadStatus.Completed
                    and not DownloadStatus.Cancelled
                    and not DownloadStatus.Error),
            DownloadGroupControlTransition.Resuming => Items.Any(item =>
                _pendingControlIds.Contains(item.Id)
                && item.StatusState == DownloadStatus.Paused),
            _ => false
        };

        if (!isWaiting)
        {
            _pendingControlIds.Clear();
            ControlTransition = DownloadGroupControlTransition.None;
        }
    }

    private DownloadStatus GetAggregateStatus()
    {
        DownloadStatus[] priority =
        [
            DownloadStatus.Error,
            DownloadStatus.Retrying,
            DownloadStatus.Merging,
            DownloadStatus.Downloading,
            DownloadStatus.Connecting,
            DownloadStatus.Queued,
            DownloadStatus.Paused,
            DownloadStatus.Completed
        ];

        return priority.FirstOrDefault(status =>
            Items.Any(item => item.StatusState == status));
    }

    private void RefreshStatusText()
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
                DownloadStatus.Retrying => "download_status_retrying_title",
                DownloadStatus.Error => "download_status_error_title",
                _ => "?"
            },
            string.Empty);
    }

    private void LanguageBase_LanguageChanged(string language)
    {
        ItemCountText = LanguageBase.GetLangValue(
            "page_torrents_file_count",
            Items.Count);
        RefreshStatusText();
    }

    private static bool Contains(string? value, string keyword) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);

    private static string FormatSpeed(double bytesPerSecond) =>
        $"{FormatBytes((long)Math.Max(0, bytesPerSecond))}/s";

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
