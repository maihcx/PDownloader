using System.Collections;
using System.Drawing.Printing;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace PDownloader.Runner.ViewModels.Windows
{
    public partial class DownloaderProgressViewModel : ObservableObject
    {
        private bool _isInitialized = false;

        private DownloaderService _downloaderService;

        [ObservableProperty]
        private RunnerConfig _runnerConfig;

        [ObservableProperty]
        private DownloaderServiceStatus _downloaderStatus;

        [ObservableProperty]
        private double _progressPercent;

        [ObservableProperty]
        private double _progressRatio;

        [ObservableProperty]
        private string _ProgressText = string.Empty;

        [ObservableProperty]
        private string _SpeedText = string.Empty;

        [ObservableProperty]
        private string _etaText = string.Empty;

        [ObservableProperty]
        private string _downloadedText = string.Empty;

        [ObservableProperty]
        private string _totalText = string.Empty;

        [ObservableProperty]
        private string _statusText = string.Empty;

        [ObservableProperty]
        private bool _isActionButtonEnabled = true;

        private string CompletedFilePath = string.Empty;

        partial void OnProgressPercentChanged(double value)
        {
            ProgressRatio = ProgressPercent / 100.0;
        }

        public DownloaderProgressViewModel(RunnerConfig runnerConfig, DownloaderService downloaderService)
        {
            RunnerConfig = runnerConfig;
            _downloaderService = downloaderService;
            _downloaderStatus = downloaderService.DownloaderStatus;

            if (!_isInitialized)
                InitializeViewModel();
        }

        private void InitializeViewModel()
        {
            _isInitialized = true;

            DownloaderStatus.State = RunnerState.Downloading;
            _downloaderService.OnProgress += _downloaderService_OnProgress;
        }

        private void _downloaderService_OnProgress(DownloadItemDto obj)
        {
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ProgressPercent  = obj.Progress;
                ProgressText     = $"{ProgressPercent:F0}%";
                SpeedText        = obj.SpeedFormatted;
                EtaText          = obj.EtaFormatted;
                DownloadedText   = obj.DownloadedFormatted;
                TotalText        = obj.TotalFormatted;

                Enum.TryParse(obj.Status, out DownloadStatus Status);
                switch (Status)
                {
                    case DownloadStatus.Queued:
                        StatusText = LanguageBase.GetLangValue("download_status_queued_title");
                        DownloaderStatus.State = RunnerState.Form;
                        IsActionButtonEnabled = false;
                        break;

                    case DownloadStatus.Connecting:
                        StatusText = LanguageBase.GetLangValue("download_status_connecting_title");
                        IsActionButtonEnabled = false;
                        break;

                    case DownloadStatus.Downloading:
                        StatusText = LanguageBase.GetLangValue("download_status_downloading_title");
                        DownloaderStatus.State = RunnerState.Downloading;
                        IsActionButtonEnabled = true;
                        break;

                    case DownloadStatus.Paused:
                        StatusText = LanguageBase.GetLangValue("download_status_paused_title");
                        IsActionButtonEnabled = true;
                        break;

                    case DownloadStatus.Merging:
                        StatusText = LanguageBase.GetLangValue("download_status_merging_title");
                        IsActionButtonEnabled = false;
                        break;

                    case DownloadStatus.Completed:
                        StatusText = LanguageBase.GetLangValue("download_status_completed_title");
                        CompletedFilePath = obj.SavePath;
                        DownloaderStatus.State = RunnerState.Completed;
                        break;

                    case DownloadStatus.Cancelled:
                        StatusText = LanguageBase.GetLangValue("download_status_cancelled_title");
                        DownloaderStatus.State = RunnerState.Cancelled;
                        Application.Current.Shutdown();
                        IsActionButtonEnabled = false;
                        break;

                    case DownloadStatus.Error:
                        StatusText = LanguageBase.GetLangValue("download_status_error_title", obj.ErrorMessage);
                        IsActionButtonEnabled = false;
                        break;
                }

            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        [RelayCommand]
        private void CancelDownload()
        {
            IsActionButtonEnabled = false;
            _downloaderService.CancelDownload();
        }

        [RelayCommand]
        private void OpenFile()
        {
            if (!File.Exists(CompletedFilePath)) return;
            Process.Start(new ProcessStartInfo(CompletedFilePath) { UseShellExecute = true });
        }

        [RelayCommand]
        private void OpenFolder()
        {
            var folder = Path.GetDirectoryName(CompletedFilePath);
            if (folder is null || !Directory.Exists(folder)) return;
            Process.Start("explorer.exe", $"/select,\"{CompletedFilePath}\"");
        }

        [RelayCommand]
        private void Pause()
        {
            IsActionButtonEnabled = false;
            _downloaderService.PauseDownload();
        }

        [RelayCommand]
        private void Resume()
        {
            IsActionButtonEnabled = false;
            _downloaderService.ResumeDownload();
        }
    }
}
