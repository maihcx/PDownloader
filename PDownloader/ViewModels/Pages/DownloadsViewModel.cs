using PDownloader.Services.DownloadServices;

namespace PDownloader.ViewModels.Pages
{
    public partial class DownloadsViewModel : ObservableObject, INavigationAware
    {
        public ObservableCollection<DownloadItemDto> Downloads { get; } = new();

        [ObservableProperty]
        private bool _isLoading = false;

        [ObservableProperty]
        private bool _isEmpty = true;

        [ObservableProperty]
        private string _statusText = "Ready";

        public DownloadsViewModel(DownloadsChannelService downloadsChannelService)
        {
            downloadsChannelService.OnProgress += OnProgress;
            downloadsChannelService.OnList     += OnList;
        }

        public Task OnNavigatedToAsync()
        {
            RequestRefresh();

            return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync()
        {
            return Task.CompletedTask;
        }

        public void RequestRefresh()
        {
            IsLoading = true;

            ConfluxManager.cfsPDownloaderCore?.Send("downloader-svc-getlist", string.Empty);
        }

        private void OnList(List<DownloadItemDto> items)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                Downloads.Clear();
                foreach (var item in items)
                {
                    Downloads.Add(item);
                }

                IsEmpty = Downloads.Count == 0;
                IsLoading = false;
                StatusText = LanguageBase.GetLangValue("task_num_title", Downloads.Count);
            });
        }

        private void OnProgress(DownloadItemDto dto)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                var existing = Downloads.FirstOrDefault(d => d.Id == dto.Id);
                if (existing != null)
                {
                    if (dto.Status == "Cancelled")
                    {
                        Downloads.Remove(existing);
                    }
                    else
                    {
                        int idx = Downloads.IndexOf(existing);
                        Downloads[idx] = dto;
                    }
                }
                else
                {
                    if (dto.Status != "Cancelled")
                    {
                        Downloads.Insert(0, dto);
                    }
                }
                IsEmpty = Downloads.Count == 0;

                StatusText = LanguageBase.GetLangValue("task_num_title", Downloads.Count);
            });
        }

        [RelayCommand]
        private void Pause(DownloadItemDto? item)
        {
            if (item == null) return;
            ConfluxManager.cfsPDownloaderCore?.Send("runner-pause", item.Id);
        }

        [RelayCommand]
        private void Resume(DownloadItemDto? item)
        {
            if (item == null) return;

            _ = Enum.TryParse(item.Status, out DownloadStatus status);
            if (status == DownloadStatus.Completed)
            {
                OpenFile(item);
            }
            else if (status == DownloadStatus.Paused)
            {
                ConfluxManager.cfsPDownloaderCore?.Send("runner-resume", item.Id);
            }
        }

        [RelayCommand]
        private void Cancel(DownloadItemDto? item)
        {
            if (item == null) return;
            ConfluxManager.cfsPDownloaderCore?.Send("runner-cancel", item.Id);
        }

        [RelayCommand]
        private void Retry(DownloadItemDto? item)
        {
            if (item == null) return;
            ConfluxManager.cfsPDownloaderCore?.Send("runner-retry", item.Id);
        }

        [RelayCommand]
        private void OpenFile(DownloadItemDto? item)
        {
            if (item == null || !System.IO.File.Exists(item.SavePath)) return;
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(item.SavePath) { UseShellExecute = true });
        }

        [RelayCommand]
        private void OpenFolder(DownloadItemDto? item)
        {
            if (item == null)
            {
                return;
            }

            var folder = Path.GetDirectoryName(item.SavePath);
            if (folder is null || !Directory.Exists(folder))
            {
                return;
            }

            Process.Start("explorer.exe", $"/select,\"{item.SavePath}\"");
        }

        [RelayCommand]
        private void Refresh() => RequestRefresh();
    }
}
