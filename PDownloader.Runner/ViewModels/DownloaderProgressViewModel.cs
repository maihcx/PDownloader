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

namespace PDownloader.Runner.ViewModels.Windows;

public partial class DownloaderProgressViewModel : ObservableObject
{
    private bool _isInitialized = false;

    private readonly DownloaderService _downloaderService;
    private readonly ProgressWindowBehaviorSettingsService _progressWindowSettingsService;
    private string _currentProgressVisualizationStage = string.Empty;
    private bool _shutdownRequested;

    [ObservableProperty]
    private RunnerConfig _runnerConfig;

    [ObservableProperty]
    private DownloaderServiceStatus _downloaderStatus;

    [ObservableProperty]
    private DownloadStatus _currentDownloadStatus = DownloadStatus.Connecting;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private double _progressRatio;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private string _speedText = string.Empty;

    [ObservableProperty]
    private string _etaText = string.Empty;

    [ObservableProperty]
    private string _downloadedText = string.Empty;

    [ObservableProperty]
    private string _totalText = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isPauseResumeButtonEnabled = true;

    [ObservableProperty]
    private bool _isCancelButtonEnabled = true;

    [ObservableProperty]
    private bool _isRetryButtonEnabled = true;

    [ObservableProperty]
    private bool _isThreadProgressVisible;

    [ObservableProperty]
    private bool _isThreadProgressUnsupportedVisible;

    [ObservableProperty]
    private bool _isThreadVisualizationLayoutExpanded;

    [ObservableProperty]
    private string _threadProgressTitle = string.Empty;

    [ObservableProperty]
    private string _threadProgressUnsupportedText = string.Empty;

    [ObservableProperty]
    private bool _isCompactStatusVisible = true;

    [ObservableProperty]
    private string _compactStatusTitle = string.Empty;

    [ObservableProperty]
    private string _compactStatusDescription = string.Empty;

    [ObservableProperty]
    private ObservableCollection<object> _threadProgress = new ObservableCollection<object>();

    private string CompletedFilePath = string.Empty;

    partial void OnProgressPercentChanged(double value)
    {
        ProgressRatio = ProgressPercent / 100.0;
    }

    public DownloaderProgressViewModel(
        RunnerConfig runnerConfig,
        DownloaderService downloaderService,
        ProgressWindowBehaviorSettingsService progressWindowSettingsService)
    {
        RunnerConfig = runnerConfig;
        _downloaderService = downloaderService;
        _progressWindowSettingsService = progressWindowSettingsService;
        _downloaderStatus = downloaderService.DownloaderStatus;

        if (!_isInitialized)
        {
            InitializeViewModel();
        }
    }

    private void InitializeViewModel()
    {
        _isInitialized = true;

        DownloaderStatus.State = RunnerState.Downloading;
        ThreadProgressUnsupportedText = LanguageBase.GetLangValue(
            "download_thread_visualization_unsupported_ytdlp");
        CompactStatusTitle = LanguageBase.GetLangValue(
            "download_status_connecting_title");
        CompactStatusDescription = LanguageBase.GetLangValue(
            "download_compact_status_no_threads_description");
        _downloaderService.OnProgress += _downloaderService_OnProgress;
    }

    private void _downloaderService_OnProgress(DownloadItemDto obj)
    {
        System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            ProgressPercent = obj.Progress;
            ProgressText = $"{ProgressPercent:F0}%";
            SpeedText = obj.SpeedFormatted;
            EtaText = obj.EtaFormatted;
            DownloadedText = obj.DownloadedFormatted;
            TotalText = obj.TotalFormatted;

            UpdateProgressVisualization(obj);

            Enum.TryParse(obj.Status, ignoreCase: true, out DownloadStatus status);
            CurrentDownloadStatus = status;

            IsRetryButtonEnabled = false;
            DownloaderStatus.HasError = false;

            switch (status)
            {
                case DownloadStatus.Queued:
                    StatusText = LanguageBase.GetLangValue("download_status_queued_title");
                    DownloaderStatus.State = RunnerState.Form;
                    IsPauseResumeButtonEnabled = false;
                    IsCancelButtonEnabled = false;
                    break;

                case DownloadStatus.Connecting:
                    StatusText = LanguageBase.GetLangValue("download_status_connecting_title");
                    DownloaderStatus.State = RunnerState.Downloading;
                    IsPauseResumeButtonEnabled = false;
                    IsCancelButtonEnabled = false;
                    break;

                case DownloadStatus.Downloading:
                    StatusText = LanguageBase.GetLangValue("download_status_downloading_title");
                    DownloaderStatus.State = RunnerState.Downloading;
                    IsPauseResumeButtonEnabled = obj.CanPause;
                    IsCancelButtonEnabled = true;
                    break;

                case DownloadStatus.Paused:
                    StatusText = LanguageBase.GetLangValue("download_status_paused_title");
                    DownloaderStatus.State = RunnerState.Downloading;
                    IsPauseResumeButtonEnabled = obj.CanResume;
                    IsCancelButtonEnabled = true;
                    break;

                case DownloadStatus.Merging:
                    StatusText = LanguageBase.GetLangValue("download_status_merging_title");
                    DownloaderStatus.State = RunnerState.Downloading;
                    IsPauseResumeButtonEnabled = obj.CanPause;
                    IsCancelButtonEnabled = false;
                    break;

                case DownloadStatus.Completed:
                    ProgressPercent = 100;
                    ProgressText = "100%";
                    SpeedText = "–";
                    EtaText = "–";
                    StatusText = LanguageBase.GetLangValue("download_status_completed_title");
                    CompletedFilePath = obj.SavePath;
                    DownloaderStatus.State = RunnerState.Completed;
                    IsPauseResumeButtonEnabled = false;
                    IsCancelButtonEnabled = false;
                    break;

                case DownloadStatus.Cancelled:
                    StatusText = LanguageBase.GetLangValue("download_status_cancelled_title");
                    DownloaderStatus.State = RunnerState.Cancelled;
                    Application.Current.Shutdown();
                    IsPauseResumeButtonEnabled = false;
                    IsCancelButtonEnabled = false;
                    break;

                case DownloadStatus.Error:
                    StatusText = LanguageBase.GetLangValue(
                        "download_status_error_title",
                        obj.ErrorMessage);
                    DownloaderStatus.HasError = true;
                    IsPauseResumeButtonEnabled = false;
                    IsCancelButtonEnabled = true;
                    IsRetryButtonEnabled = true;
                    break;

                case DownloadStatus.Retrying:
                    StatusText = LanguageBase.GetLangValue(
                        "download_status_retry_title",
                        obj.ErrorMessage);
                    IsPauseResumeButtonEnabled = false;
                    IsCancelButtonEnabled = true;
                    break;
            }

            UpdateCompactStatusPanel(status, obj);

            if (status == DownloadStatus.Completed
                && _progressWindowSettingsService
                    .GetCurrent()
                    .CloseProgressWindowWhenDownloadCompletes)
            {
                RequestApplicationShutdown();
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void UpdateProgressVisualization(DownloadItemDto item)
    {
        string mode = string.IsNullOrWhiteSpace(item.ProgressVisualizationMode)
            ? "None"
            : item.ProgressVisualizationMode;
        string stage = item.ProgressVisualizationStage ?? string.Empty;
        List<DownloadThreadProgressDto> threadProgress =
            item.ThreadProgress ?? new List<DownloadThreadProgressDto>();

        if (mode.Equals("Unsupported", StringComparison.OrdinalIgnoreCase))
        {
            IsThreadVisualizationLayoutExpanded = false;
            IsThreadProgressVisible = false;
            IsThreadProgressUnsupportedVisible = true;
            ThreadProgressUnsupportedText = GetUnsupportedVisualizationText(stage);
            ClearThreadProgress();
            return;
        }

        if (!mode.Equals("Threads", StringComparison.OrdinalIgnoreCase))
        {
            IsThreadVisualizationLayoutExpanded = false;
            IsThreadProgressVisible = false;
            IsThreadProgressUnsupportedVisible = false;
            ClearThreadProgress();
            return;
        }

        List<DownloadThreadProgressDto> visibleThreads = threadProgress
            .OfType<DownloadThreadProgressDto>()
            .OrderBy(progress => progress.Index)
            .ToList();

        if (visibleThreads.Count < 2)
        {
            IsThreadVisualizationLayoutExpanded = false;
            IsThreadProgressVisible = false;
            IsThreadProgressUnsupportedVisible = false;
            ClearThreadProgress();
            return;
        }

        IsThreadVisualizationLayoutExpanded = true;
        IsThreadProgressUnsupportedVisible = false;
        ThreadProgressTitle = GetThreadProgressTitle(stage);

        if (!_currentProgressVisualizationStage.Equals(
            stage,
            StringComparison.OrdinalIgnoreCase))
        {
            ClearThreadProgress();
            _currentProgressVisualizationStage = stage;
        }

        ThreadProgress.Clear();
        foreach (DownloadThreadProgressDto progress in visibleThreads)
        {
            ThreadProgress.Add(CreateThreadProgressItem(progress));
        }

        IsThreadProgressVisible = true;
    }

    private void UpdateCompactStatusPanel(DownloadStatus status, DownloadItemDto item)
    {
        if (IsThreadProgressVisible)
        {
            IsCompactStatusVisible = false;
            return;
        }

        IsCompactStatusVisible = true;

        string mode = string.IsNullOrWhiteSpace(item.ProgressVisualizationMode)
            ? "None"
            : item.ProgressVisualizationMode;
        string stage = item.ProgressVisualizationStage ?? string.Empty;
        int threadCount = item.ThreadProgress?.Count ?? 0;

        if (status == DownloadStatus.Completed)
        {
            CompactStatusTitle = LanguageBase.GetLangValue(
                "download_compact_status_completed_title");
            CompactStatusDescription = LanguageBase.GetLangValue(
                "download_compact_status_completed_description");
            return;
        }

        if (status == DownloadStatus.Paused && item.IsMergeProgressActive)
        {
            CompactStatusTitle = LanguageBase.GetLangValue(
                "download_compact_status_paused_merge_title");
            CompactStatusDescription = LanguageBase.GetLangValue(
                "download_compact_status_paused_merge_description",
                Math.Clamp(item.Progress, 0, 100).ToString("F0"));
            return;
        }

        if (status == DownloadStatus.Merging)
        {
            CompactStatusTitle = string.IsNullOrWhiteSpace(StatusText)
                ? LanguageBase.GetLangValue("download_status_merging_title")
                : StatusText;
            CompactStatusDescription = LanguageBase.GetLangValue(
                "download_compact_status_merging_description");
            return;
        }

        if (status == DownloadStatus.Paused)
        {
            CompactStatusTitle = string.IsNullOrWhiteSpace(StatusText)
                ? LanguageBase.GetLangValue("download_status_paused_title")
                : StatusText;
            CompactStatusDescription = LanguageBase.GetLangValue(
                "download_compact_status_paused_download_description");
            return;
        }

        if (status == DownloadStatus.Queued)
        {
            CompactStatusTitle = string.IsNullOrWhiteSpace(StatusText)
                ? LanguageBase.GetLangValue("download_status_queued_title")
                : StatusText;
            CompactStatusDescription = LanguageBase.GetLangValue(
                "download_compact_status_queued_description");
            return;
        }

        if (status == DownloadStatus.Connecting)
        {
            CompactStatusTitle = string.IsNullOrWhiteSpace(StatusText)
                ? LanguageBase.GetLangValue("download_status_connecting_title")
                : StatusText;
            CompactStatusDescription = LanguageBase.GetLangValue(
                "download_compact_status_connecting_description");
            return;
        }

        if (status == DownloadStatus.Error)
        {
            CompactStatusTitle = string.IsNullOrWhiteSpace(StatusText)
                ? LanguageBase.GetLangValue("download_status_error_title", item.ErrorMessage)
                : StatusText;
            CompactStatusDescription = LanguageBase.GetLangValue(
                "download_compact_status_error_description");
            return;
        }

        if (mode.Equals("Unsupported", StringComparison.OrdinalIgnoreCase))
        {
            CompactStatusTitle = LanguageBase.GetLangValue(
                "download_compact_status_unsupported_title");
            CompactStatusDescription = GetUnsupportedVisualizationText(stage);
            return;
        }

        if (mode.Equals("Threads", StringComparison.OrdinalIgnoreCase)
            && threadCount == 1)
        {
            CompactStatusTitle = LanguageBase.GetLangValue(
                "download_compact_status_single_thread_title");
            CompactStatusDescription = LanguageBase.GetLangValue(
                "download_compact_status_single_thread_description");
            return;
        }

        CompactStatusTitle = string.IsNullOrWhiteSpace(StatusText)
            ? LanguageBase.GetLangValue("download_status_downloading_title")
            : StatusText;
        CompactStatusDescription = LanguageBase.GetLangValue(
            "download_compact_status_no_threads_description");
    }

    private static object CreateThreadProgressItem(DownloadThreadProgressDto source)
    {
        long downloadedBytes = Math.Max(0, source.DownloadedBytes);
        long totalBytes = Math.Max(0, source.TotalBytes);
        double speedBps = Math.Max(0, source.SpeedBps);
        double progress = Math.Clamp(source.Progress, 0, 100);
        string state = source.State ?? string.Empty;
        int currentUnit = Math.Max(0, source.CurrentUnit);
        int totalUnits = Math.Max(0, source.TotalUnits);

        return new
        {
            source.Index,
            Number = source.Index + 1,
            Title = LanguageBase.GetLangValue(
                "download_thread_item_title",
                source.Index + 1),
            DownloadedBytes = downloadedBytes,
            TotalBytes = totalBytes,
            SpeedBps = speedBps,
            Progress = progress,
            State = state,
            CurrentUnit = currentUnit,
            TotalUnits = totalUnits,
            IsIndeterminate = totalBytes <= 0
                && state.Equals("Downloading", StringComparison.OrdinalIgnoreCase),
            StateText = GetThreadStateText(state),
            DetailText = currentUnit > 0 && totalUnits > 0
                ? LanguageBase.GetLangValue(
                    "download_thread_fragment_detail",
                    currentUnit,
                    totalUnits)
                : string.Empty,
            SpeedText = speedBps > 0
                ? $"{FormatBytes((long)speedBps)}/s"
                : "–",
            BytesText = totalBytes > 0
                ? $"{FormatBytes(downloadedBytes)} / {FormatBytes(totalBytes)}"
                : FormatBytes(downloadedBytes),
            ProgressText = totalBytes > 0
                ? $"{progress:F0}%"
                : "–"
        };
    }

    private static string GetThreadStateText(string state)
    {
        return state.ToLowerInvariant() switch
        {
            "waiting" => LanguageBase.GetLangValue("download_thread_state_waiting"),
            "downloading" => LanguageBase.GetLangValue("download_thread_state_downloading"),
            "retrying" => LanguageBase.GetLangValue("download_thread_state_retrying"),
            "completed" => LanguageBase.GetLangValue("download_thread_state_completed"),
            "failed" => LanguageBase.GetLangValue("download_thread_state_failed"),
            _ => state
        };
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:F1} KB";
        }

        if (bytes < 1024L * 1024 * 1024)
        {
            return $"{bytes / (1024.0 * 1024):F1} MB";
        }

        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    private string GetThreadProgressTitle(string stage)
    {
        string stageText = stage?.ToLowerInvariant() switch
        {
            "video" => LanguageBase.GetLangValue("download_thread_stage_video"),
            "audio" => LanguageBase.GetLangValue("download_thread_stage_audio"),
            "hlsfragments" => LanguageBase.GetLangValue("download_thread_stage_hls"),
            _ => LanguageBase.GetLangValue("download_thread_stage_file")
        };

        return LanguageBase.GetLangValue(
            "download_thread_visualization_title",
            stageText);
    }

    private static string GetUnsupportedVisualizationText(string? stage)
    {
        return string.Equals(stage, "YtDlp", StringComparison.OrdinalIgnoreCase)
            ? LanguageBase.GetLangValue(
                "download_thread_visualization_unsupported_ytdlp")
            : LanguageBase.GetLangValue(
                "download_thread_visualization_unsupported_generic");
    }

    private void ClearThreadProgress()
    {
        ThreadProgress.Clear();
        _currentProgressVisualizationStage = string.Empty;
    }

    [RelayCommand]
    private void CancelDownload()
    {
        IsPauseResumeButtonEnabled = false;
        IsCancelButtonEnabled = false;
        _downloaderService.CancelDownload();
    }

    [RelayCommand]
    private void OpenFile()
    {
        bool opened = ShellProcessLauncher.OpenFile(CompletedFilePath);
        if (opened
            && _progressWindowSettingsService
                .GetCurrent()
                .CloseProgressWindowAfterOpeningFile)
        {
            RequestApplicationShutdown();
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        bool opened = ShellProcessLauncher.OpenContainingFolder(CompletedFilePath);
        if (opened
            && _progressWindowSettingsService
                .GetCurrent()
                .CloseProgressWindowAfterOpeningFolder)
        {
            RequestApplicationShutdown();
        }
    }

    private void RequestApplicationShutdown()
    {
        if (_shutdownRequested)
        {
            return;
        }

        _shutdownRequested = true;
        Application application = Application.Current;
        application.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => application.Shutdown()));
    }

    [RelayCommand]
    private void Pause()
    {
        if (!IsPauseResumeButtonEnabled)
        {
            return;
        }

        IsPauseResumeButtonEnabled = false;
        _downloaderService.PauseDownload();
    }

    [RelayCommand]
    private void Resume()
    {
        if (!IsPauseResumeButtonEnabled)
        {
            return;
        }

        IsPauseResumeButtonEnabled = false;
        _downloaderService.ResumeDownload();
    }

    [RelayCommand]
    private void Retry()
    {
        if (!IsRetryButtonEnabled)
        {
            return;
        }

        IsRetryButtonEnabled = false;
        _downloaderService.RetryDownload();
    }
}
