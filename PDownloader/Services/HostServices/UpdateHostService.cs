namespace PDownloader.Services.HostServices
{
    public sealed class UpdateHostService : INotifyPropertyChanged, IDisposable
    {
        private readonly UpdateService _update;
        private CancellationTokenSource? _cts;

        private UpdateStatus _status = UpdateStatus.Idle;
        public UpdateStatus Status
        {
            get => _status;
            private set => SetField(ref _status, value);
        }

        private double _downloadProgress;
        public double DownloadProgress
        {
            get => _downloadProgress;
            private set => SetField(ref _downloadProgress, value);
        }

        public GitHubRelease? LatestRelease => _update.LatestRelease;
        public string? ErrorMessage => _update.ErrorMessage;
        public long InstallerSize => _update.InstallerSize;

        public event PropertyChangedEventHandler? PropertyChanged;

        public UpdateHostService(UpdateService update)
        {
            _update = update;
        }

        public async Task CheckAsync(Action<GitHubRelease>? onUpdateFound = null)
        {
            if (Status is UpdateStatus.Checking or UpdateStatus.Downloading)
            {
                return;
            }

            Cancel();
            _cts = new CancellationTokenSource();

            Status = UpdateStatus.Checking;

            try
            {
                bool hasUpdate = await _update.CheckForUpdateAsync(_cts.Token);

                OnPropertyChanged(nameof(LatestRelease));
                OnPropertyChanged(nameof(InstallerSize));

                if (hasUpdate)
                {
                    Status = UpdateStatus.UpdateAvailable;
                    onUpdateFound?.Invoke(_update.LatestRelease!);
                }
                else
                {
                    Status = UpdateStatus.UpToDate;
                }
            }
            catch (OperationCanceledException)
            {
                Status = UpdateStatus.Idle;
            }
            catch
            {
                OnPropertyChanged(nameof(ErrorMessage));
                Status = UpdateStatus.Error;
            }
        }

        public async Task DownloadAsync()
        {
            if (Status is not UpdateStatus.UpdateAvailable)
            {
                return;
            }

            Cancel();
            _cts = new CancellationTokenSource();

            DownloadProgress = 0;
            Status = UpdateStatus.Downloading;

            var progress = new Progress<double>(p =>
            {
                DownloadProgress = p;
            });

            try
            {
                await _update.DownloadInstallerAsync(progress, _cts.Token);
                DownloadProgress = 1.0;
                Status = UpdateStatus.ReadyToInstall;
            }
            catch (OperationCanceledException)
            {
                DownloadProgress = 0;
                Status = UpdateStatus.UpdateAvailable;
            }
            catch
            {
                OnPropertyChanged(nameof(ErrorMessage));
                Status = UpdateStatus.Error;
            }
        }

        public void LaunchInstaller()
        {
            if (Status is not UpdateStatus.ReadyToInstall)
            {
                return;
            }

            _update.LaunchInstaller();
        }

        public void Cancel()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(name);
            return true;
        }

        public void Dispose()
        {
            Cancel();
            PropertyChanged = null;
        }
    }
}