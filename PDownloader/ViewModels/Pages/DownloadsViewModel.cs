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

namespace PDownloader.ViewModels.Pages;

public partial class DownloadsViewModel : ObservableObject, INavigationAware
{
    public ObservableCollection<DownloadItemDto> Downloads { get; } = new();

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private string _statusText = "Ready";

    private readonly DownloadLauncherService _downloadLauncherService;

    public DownloadsViewModel(DownloadsChannelService downloadsChannelService, DownloadLauncherService downloadLauncherService)
    {
        downloadsChannelService.OnProgress += OnProgress;
        downloadsChannelService.OnList += OnList;
        _downloadLauncherService = downloadLauncherService;
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
            foreach (DownloadItemDto item in items)
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
            DownloadItemDto? existing = Downloads.FirstOrDefault(d => d.Id == dto.Id);
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
        if (item == null)
        {
            return;
        }

        ConfluxManager.cfsPDownloaderCore?.Send("runner-pause", item.Id);
    }

    [RelayCommand]
    private void Resume(DownloadItemDto? item)
    {
        if (item == null)
        {
            return;
        }

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
        if (item == null)
        {
            return;
        }

        ConfluxManager.cfsPDownloaderCore?.Send("runner-cancel", item.Id);
    }

    [RelayCommand]
    private void Retry(DownloadItemDto? item)
    {
        if (item == null)
        {
            return;
        }

        ConfluxManager.cfsPDownloaderCore?.Send("runner-retry", item.Id);
    }

    [RelayCommand]
    private void OpenFile(DownloadItemDto? item)
    {
        if (item == null || !File.Exists(item.SavePath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(item.SavePath) { UseShellExecute = true });
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
    private async Task Add()
    {
        Dialogs.ViewModels.AddLink? result = await MessengerService.ShowDialogAsync<Dialogs.Views.AddLink, Dialogs.ViewModels.AddLink>();

        if (result != null)
        {
            _downloadLauncherService.RequestDownload(result.Link);
        }
    }

    [RelayCommand]
    private void Refresh() => RequestRefresh();
}
