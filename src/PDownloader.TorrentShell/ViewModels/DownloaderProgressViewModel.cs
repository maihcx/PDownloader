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

public enum TorrentShellControlTransition
{
    None,
    Pausing,
    Resuming
}

public partial class DownloaderProgressViewModel : ObservableObject
{
    private readonly TorrentShellService _torrentShellService;
    private readonly HashSet<string> _pendingControlIds = new(StringComparer.Ordinal);
    private int _shutdownRequested;

    [ObservableProperty]
    private TorrentShellConfig _torrentConfig;

    [ObservableProperty]
    private double _overallProgress;

    [ObservableProperty]
    private string _overallDownloadedText = "0 B";

    [ObservableProperty]
    private string _overallTotalText = "0 B";

    [ObservableProperty]
    private string _overallSpeedText = "–";

    [ObservableProperty]
    private string _progressSummary = string.Empty;

    [ObservableProperty]
    private bool _isComplete;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPauseControlVisible))]
    [NotifyPropertyChangedFor(nameof(IsResumeControlVisible))]
    [NotifyPropertyChangedFor(nameof(IsCancelControlVisible))]
    [NotifyPropertyChangedFor(nameof(CanPauseAll))]
    [NotifyPropertyChangedFor(nameof(CanResumeAll))]
    private DownloadStatus _overallStatus = DownloadStatus.Queued;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPauseControlVisible))]
    [NotifyPropertyChangedFor(nameof(IsResumeControlVisible))]
    private bool _hasActiveDownloads;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPauseControlVisible))]
    [NotifyPropertyChangedFor(nameof(IsResumeControlVisible))]
    [NotifyPropertyChangedFor(nameof(CanPauseAll))]
    [NotifyPropertyChangedFor(nameof(CanResumeAll))]
    private TorrentShellControlTransition _controlTransition;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPauseAll))]
    [NotifyPropertyChangedFor(nameof(CanResumeAll))]
    [NotifyPropertyChangedFor(nameof(CanCancelAll))]
    private bool _isCancelPending;

    public ObservableCollection<TorrentFileViewModel> Files { get; } = [];

    public bool IsPauseControlVisible =>
        ControlTransition == TorrentShellControlTransition.Pausing
        || (ControlTransition != TorrentShellControlTransition.Resuming
            && HasActiveDownloads);

    public bool IsResumeControlVisible =>
        ControlTransition == TorrentShellControlTransition.Resuming
        || (ControlTransition != TorrentShellControlTransition.Pausing
            && !HasActiveDownloads
            && Files.Any(file => file.CanResume));

    public bool IsCancelControlVisible =>
        !IsComplete && OverallStatus != DownloadStatus.Cancelled;

    public bool AreDownloadStatesSynchronized =>
        !HasRunningFiles || !HasPausedFiles;

    public bool CanPauseAll =>
        !IsCancelPending
        && ControlTransition == TorrentShellControlTransition.None
        && AreDownloadStatesSynchronized
        && Files.Any(file => file.CanPause)
        && OverallStatus != DownloadStatus.Connecting;

    public bool CanResumeAll =>
        !IsCancelPending
        && ControlTransition == TorrentShellControlTransition.None
        && AreDownloadStatesSynchronized
        && Files.Any(file => file.CanResume);

    public bool CanCancelAll =>
        !IsCancelPending && Files.Any(file => file.CanCancel);

    public DownloaderProgressViewModel(
        TorrentShellConfig torrentConfig,
        TorrentShellService torrentShellService)
    {
        _torrentConfig = torrentConfig;
        _torrentShellService = torrentShellService;
        _torrentShellService.ProgressReceived += TorrentShellService_ProgressReceived;

        foreach (TorrentFileViewModel file in TorrentConfig.Files.Where(file => file.IsSelected))
        {
            file.PropertyChanged += File_PropertyChanged;
            Files.Add(file);
            if (_torrentShellService.TryGetLatestProgress(file.DownloadId, out DownloadItemDto? progress)
                && progress is not null)
            {
                file.ApplyProgress(progress);
            }
        }

        TranslationSource.Instance.PropertyChanged += TranslationSource_PropertyChanged;
        RefreshSummary();
    }

    [RelayCommand]
    private void Pause(TorrentFileViewModel? file)
    {
        if (file?.CanPause == true)
        {
            _torrentShellService.Pause(file.DownloadId);
        }
    }

    [RelayCommand]
    private void Resume(TorrentFileViewModel? file)
    {
        if (file?.CanResume == true)
        {
            _torrentShellService.Resume(file.DownloadId);
        }
    }

    [RelayCommand]
    private void Retry(TorrentFileViewModel? file)
    {
        if (file?.HasError == true)
        {
            _torrentShellService.Retry(file.DownloadId);
        }
    }

    [RelayCommand]
    private void Cancel(TorrentFileViewModel? file)
    {
        if (file?.CanCancel == true)
        {
            _torrentShellService.Cancel(file.DownloadId);
        }
    }

    [RelayCommand(CanExecute = nameof(CanPauseAll))]
    private void PauseAll()
    {
        TorrentFileViewModel[] targets = Files
            .Where(file => file.CanPause)
            .ToArray();
        if (targets.Length == 0)
        {
            return;
        }

        BeginControlTransition(
            TorrentShellControlTransition.Pausing,
            targets.Select(file => file.DownloadId));
        foreach (TorrentFileViewModel file in targets)
        {
            _torrentShellService.Pause(file.DownloadId);
        }

        RefreshSummary();
    }

    [RelayCommand(CanExecute = nameof(CanResumeAll))]
    private void ResumeAll()
    {
        TorrentFileViewModel[] targets = Files
            .Where(file => file.CanResume)
            .ToArray();
        if (targets.Length == 0)
        {
            return;
        }

        BeginControlTransition(
            TorrentShellControlTransition.Resuming,
            targets.Select(file => file.DownloadId));
        foreach (TorrentFileViewModel file in targets)
        {
            _torrentShellService.Resume(file.DownloadId);
        }

        RefreshSummary();
    }

    [RelayCommand(CanExecute = nameof(CanCancelAll))]
    private void CancelAll()
    {
        TorrentFileViewModel[] targets = Files
            .Where(file => file.CanCancel)
            .ToArray();
        if (targets.Length == 0)
        {
            return;
        }

        IsCancelPending = true;
        foreach (TorrentFileViewModel file in targets)
        {
            _torrentShellService.Cancel(file.DownloadId);
        }

        NotifyAggregateCommandStates();
    }

    [RelayCommand]
    private void OpenFile(TorrentFileViewModel? file)
    {
        if (file is null || !File.Exists(file.SavePath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(file.SavePath) { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenFolder(TorrentFileViewModel? file)
    {
        if (file is null || string.IsNullOrWhiteSpace(file.SavePath))
        {
            return;
        }

        string? folder = Path.GetDirectoryName(file.SavePath);
        if (folder is null || !Directory.Exists(folder))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(
            "explorer.exe",
            $"/select,\"{file.SavePath}\"")
        {
            UseShellExecute = true
        });
    }

    private void TorrentShellService_ProgressReceived(DownloadItemDto progress)
    {
        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            TorrentFileViewModel? file = Files.FirstOrDefault(candidate =>
                string.Equals(candidate.DownloadId, progress.Id, StringComparison.Ordinal));
            file?.ApplyProgress(progress);
        }));
    }

    private void File_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TorrentFileViewModel.Progress)
            or nameof(TorrentFileViewModel.Status)
            or nameof(TorrentFileViewModel.SpeedBps)
            or nameof(TorrentFileViewModel.DownloadedText)
            or nameof(TorrentFileViewModel.TotalText)
            or nameof(TorrentFileViewModel.CanPause)
            or nameof(TorrentFileViewModel.CanResume))
        {
            RefreshSummary();
        }
    }

    private void TranslationSource_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        RefreshSummary();
    }

    private void RefreshSummary()
    {
        RefreshControlTransition();

        long totalBytes = Files.Sum(file => file.Length);
        double weightedProgress = totalBytes > 0
            ? Files.Sum(file => file.Length * file.Progress) / totalBytes
            : Files.Count > 0
                ? Files.Average(file => file.Progress)
                : 0;
        int completed = Files.Count(file => file.IsCompleted);
        double speed = Files.Sum(file => file.SpeedBps);

        OverallProgress = Math.Clamp(weightedProgress, 0, 100);
        OverallDownloadedText = TorrentFileViewModel.FormatBytes(
            (long)Math.Round(totalBytes * OverallProgress / 100.0));
        OverallTotalText = TorrentFileViewModel.FormatBytes(totalBytes);
        OverallSpeedText = speed > 0
            ? $"{TorrentFileViewModel.FormatBytes((long)speed)}/s"
            : "–";
        ProgressSummary = LanguageBase.GetLangValue(
            "torrent_shell_progress_summary",
            completed,
            Files.Count);
        IsComplete = Files.Count > 0 && completed == Files.Count;
        HasActiveDownloads = Files.Any(file => file.Status is
            DownloadStatus.Queued
            or DownloadStatus.Connecting
            or DownloadStatus.Downloading
            or DownloadStatus.Merging
            or DownloadStatus.Retrying);
        OverallStatus = GetOverallStatus();

        if (IsCancelPending && Files.All(file => !file.CanCancel))
        {
            IsCancelPending = false;
        }

        OnPropertyChanged(nameof(IsPauseControlVisible));
        OnPropertyChanged(nameof(IsResumeControlVisible));
        OnPropertyChanged(nameof(IsCancelControlVisible));
        OnPropertyChanged(nameof(AreDownloadStatesSynchronized));
        OnPropertyChanged(nameof(CanPauseAll));
        OnPropertyChanged(nameof(CanResumeAll));
        OnPropertyChanged(nameof(CanCancelAll));
        NotifyAggregateCommandStates();

        if (Files.Count > 0
            && Files.Any(file => file.Status == DownloadStatus.Cancelled)
            && Files.All(file => file.Status is
                DownloadStatus.Completed or DownloadStatus.Cancelled))
        {
            RequestShutdown();
        }
    }

    private void BeginControlTransition(
        TorrentShellControlTransition transition,
        IEnumerable<string> downloadIds)
    {
        _pendingControlIds.Clear();
        foreach (string id in downloadIds.Where(id => !string.IsNullOrWhiteSpace(id)))
        {
            _pendingControlIds.Add(id);
        }

        ControlTransition = _pendingControlIds.Count == 0
            ? TorrentShellControlTransition.None
            : transition;
    }

    private void RefreshControlTransition()
    {
        if (ControlTransition == TorrentShellControlTransition.None)
        {
            return;
        }

        _pendingControlIds.RemoveWhere(id => Files.All(file => file.DownloadId != id));
        bool isWaiting = ControlTransition switch
        {
            TorrentShellControlTransition.Pausing => Files.Any(file =>
                _pendingControlIds.Contains(file.DownloadId)
                && file.Status is not DownloadStatus.Paused
                    and not DownloadStatus.Completed
                    and not DownloadStatus.Cancelled
                    and not DownloadStatus.Error),
            TorrentShellControlTransition.Resuming => Files.Any(file =>
                _pendingControlIds.Contains(file.DownloadId)
                && file.Status == DownloadStatus.Paused),
            _ => false
        };

        if (!isWaiting)
        {
            _pendingControlIds.Clear();
            ControlTransition = TorrentShellControlTransition.None;
        }
    }

    private DownloadStatus GetOverallStatus()
    {
        if (Files.Count == 0)
        {
            return DownloadStatus.Queued;
        }

        if (Files.All(file => file.Status == DownloadStatus.Completed))
        {
            return DownloadStatus.Completed;
        }

        if (Files.All(file => file.Status is
            DownloadStatus.Completed or DownloadStatus.Cancelled))
        {
            return DownloadStatus.Cancelled;
        }

        if (ControlTransition == TorrentShellControlTransition.Resuming)
        {
            return DownloadStatus.Connecting;
        }

        if (Files.Any(file => file.Status == DownloadStatus.Paused)
            && Files.All(file => file.Status is
                DownloadStatus.Paused
                or DownloadStatus.Completed
                or DownloadStatus.Cancelled))
        {
            return DownloadStatus.Paused;
        }

        DownloadStatus[] priority =
        [
            DownloadStatus.Error,
            DownloadStatus.Retrying,
            DownloadStatus.Merging,
            DownloadStatus.Downloading,
            DownloadStatus.Connecting,
            DownloadStatus.Queued,
            DownloadStatus.Paused,
            DownloadStatus.Completed,
            DownloadStatus.Cancelled
        ];

        return priority.First(status => Files.Any(file => file.Status == status));
    }

    private bool HasRunningFiles => Files.Any(file => file.Status is
        DownloadStatus.Queued
        or DownloadStatus.Connecting
        or DownloadStatus.Downloading
        or DownloadStatus.Merging
        or DownloadStatus.Retrying);

    private bool HasPausedFiles => Files.Any(file =>
        file.Status == DownloadStatus.Paused);

    private void NotifyAggregateCommandStates()
    {
        PauseAllCommand.NotifyCanExecuteChanged();
        ResumeAllCommand.NotifyCanExecuteChanged();
        CancelAllCommand.NotifyCanExecuteChanged();
    }

    private void RequestShutdown()
    {
        if (Interlocked.Exchange(ref _shutdownRequested, 1) != 0)
        {
            return;
        }

        Application application = Application.Current;
        application.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(application.Shutdown));
    }
}
