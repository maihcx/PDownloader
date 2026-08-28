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
    private bool _isInitialized = false;

    public ObservableCollection<DownloadItemViewModel> Downloads { get; } = new();

    public ICollectionView DownloadsView { get; }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private string _searchText = string.Empty;

    public ObservableCollection<DownloadSortOption> SortOptions { get; } =
    [
        new(DownloadSortMode.NameAscending,
            "download_view_sort_filename_az_title"),
        new(DownloadSortMode.NameDescending,
            "download_view_sort_filename_za_title"),
        new(DownloadSortMode.TimeStartAscending,
            "download_view_sort_filetime_start_asc_title"),
        new(DownloadSortMode.TimeStartDescending,
            "download_view_sort_filetime_start_desc_title"),
        new(DownloadSortMode.TimeEndAscending,
            "download_view_sort_filetime_end_asc_title"),
        new(DownloadSortMode.TimeEndDescending,
            "download_view_sort_filetime_end_desc_title"),
        new(DownloadSortMode.SizeAscending,
            "download_view_sort_filesize_asc_title"),
        new(DownloadSortMode.SizeDescending,
            "download_view_sort_filesize_desc_title"),
    ];

    [ObservableProperty]
    private DownloadSortOption? _selectedSortOption;

    partial void OnSelectedSortOptionChanged(DownloadSortOption? value)
    {
        if (value == null)
        {
            return;
        }

        ApplySort(value.Mode);
    }

    [ObservableProperty]
    private bool _isFlyoutOpen = false;

    private readonly DownloadLauncherService _downloadLauncherService;

    public DownloadsViewModel(
        DownloadsChannelService downloadsChannelService,
        DownloadLauncherService downloadLauncherService)
    {
        downloadsChannelService.OnProgress += OnProgress;

        _downloadLauncherService = downloadLauncherService;

        DownloadsView = CollectionViewSource.GetDefaultView(Downloads);
        DownloadsView.Filter = FilterDownload;

        SelectedSortOption = SortOptions[0];
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
        if (!_isInitialized)
        {
            IsLoading = true;
            _isInitialized = true;
            _ = RequestRefreshAsync();
        }
    }

    private async Task RequestRefreshAsync()
    {
        ConfluxService? coreService = ConfluxManager.cfsPDownloaderCore;
        if (coreService is null)
        {
            IsLoading = false;
            _isInitialized = false;
            return;
        }

        IpcRequestResult<List<DownloadItemDto>> result =
            await coreService.RequestAsync(DownloadProtocol.GetList);

        if (!result.Success || result.Value is null)
        {
            IsLoading = false;
            _isInitialized = false;
            return;
        }

        OnList(
            result.Value
                .Select(DownloadItemViewModel.FromContract)
                .ToList());
    }

    partial void OnSearchTextChanged(string value)
    {
        DownloadsView.Refresh();
        UpdateViewState();
    }

    private bool FilterDownload(object item)
    {
        if (item is not DownloadItemViewModel download)
        {
            return false;
        }

        string keyword = SearchText.Trim();

        if (string.IsNullOrWhiteSpace(keyword))
        {
            return true;
        }

        return ContainsKeyword(download.FileName, keyword)
            || ContainsKeyword(download.Url, keyword)
            || ContainsKeyword(download.Status, keyword)
            || ContainsKeyword(download.ErrorMessage, keyword)
            || ContainsKeyword(download.SavePath, keyword);
    }

    private void ApplySort(DownloadSortMode mode)
    {
        using (DownloadsView.DeferRefresh())
        {
            DownloadsView.SortDescriptions.Clear();

            switch (mode)
            {
                case DownloadSortMode.NameAscending:
                    AddSort(
                        nameof(DownloadItemViewModel.FileName),
                        ListSortDirection.Ascending);
                    AddStableTieBreaker();
                    break;

                case DownloadSortMode.NameDescending:
                    AddSort(
                        nameof(DownloadItemViewModel.FileName),
                        ListSortDirection.Descending);
                    AddStableTieBreaker();
                    break;

                case DownloadSortMode.TimeStartAscending:
                    AddSort(
                        nameof(DownloadItemViewModel.StartTime),
                        ListSortDirection.Ascending);
                    break;

                case DownloadSortMode.TimeStartDescending:
                    AddSort(
                        nameof(DownloadItemViewModel.StartTime),
                        ListSortDirection.Descending);
                    break;

                case DownloadSortMode.TimeEndAscending:
                    AddSort(
                        nameof(DownloadItemViewModel.EndTime),
                        ListSortDirection.Ascending);
                    break;

                case DownloadSortMode.TimeEndDescending:
                    AddSort(
                        nameof(DownloadItemViewModel.EndTime),
                        ListSortDirection.Descending);
                    break;

                case DownloadSortMode.SizeAscending:
                    AddSort(
                        nameof(DownloadItemViewModel.TotalBytes),
                        ListSortDirection.Ascending);
                    break;

                case DownloadSortMode.SizeDescending:
                    AddSort(
                        nameof(DownloadItemViewModel.TotalBytes),
                        ListSortDirection.Descending);
                    break;
            }
        }

        UpdateViewState();
    }

    private void AddSort(
        string propertyName,
        ListSortDirection direction)
    {
        DownloadsView.SortDescriptions.Add(
            new SortDescription(propertyName, direction));
    }

    private void AddStableTieBreaker()
    {
        // Progress updates replace the item in the source collection. Without a
        // unique secondary key, equal file names can be reinserted at different
        // positions in the sorted view after every update.
        AddSort(
            nameof(DownloadItemViewModel.Id),
            ListSortDirection.Ascending);
    }

    private static bool ContainsKeyword(string? value, string keyword)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);
    }

    private void UpdateViewState()
    {
        int visibleCount = DownloadsView.Cast<object>().Count();

        IsEmpty = visibleCount == 0;
        StatusText = LanguageBase.GetLangValue("task_num_title", visibleCount);
    }

    private void RefreshFilteredView()
    {
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            DownloadsView.Refresh();
        }

        UpdateViewState();
    }

    private void OnList(List<DownloadItemViewModel> items)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            Downloads.Clear();

            foreach (DownloadItemViewModel item in items)
            {
                Downloads.Add(item);
            }

            IsLoading = false;

            RefreshFilteredView();
        });
    }

    private void OnProgress(DownloadItemViewModel dto)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            DownloadItemViewModel? existing = Downloads.FirstOrDefault(d => d.Id == dto.Id);
            if (existing != null)
            {
                if (dto.StatusState == DownloadStatus.Cancelled)
                {
                    Downloads.Remove(existing);
                }
                else
                {
                    int index = Downloads.IndexOf(existing);
                    Downloads[index] = dto;
                }
            }
            else if (dto.StatusState != DownloadStatus.Cancelled)
            {
                Downloads.Insert(0, dto);
            }

            RefreshFilteredView();
        });
    }

    [RelayCommand]
    private void Pause(DownloadItemViewModel? item)
    {
        if (item == null || !item.CanPause)
        {
            return;
        }

        ConfluxManager.cfsPDownloaderCore?.Send(DownloadProtocol.RunnerPause, new DownloadIdRequest(item.Id));
    }

    [RelayCommand]
    private void Resume(DownloadItemViewModel? item)
    {
        if (item == null)
        {
            return;
        }

        if (item.StatusState == DownloadStatus.Completed)
        {
            OpenFile(item);
        }
        else if (item.StatusState == DownloadStatus.Paused && item.CanResume)
        {
            ConfluxManager.cfsPDownloaderCore?.Send(DownloadProtocol.RunnerResume, new DownloadIdRequest(item.Id));
        }
    }

    [RelayCommand]
    private void Cancel(DownloadItemViewModel? item)
    {
        if (item == null)
        {
            return;
        }

        ConfluxManager.cfsPDownloaderCore?.Send(DownloadProtocol.RunnerCancel, new DownloadIdRequest(item.Id));
    }

    [RelayCommand]
    private void Retry(DownloadItemViewModel? item)
    {
        if (item == null)
        {
            return;
        }

        ConfluxManager.cfsPDownloaderCore?.Send(DownloadProtocol.RunnerRetry, new DownloadIdRequest(item.Id));
    }

    [RelayCommand]
    private void OpenFile(DownloadItemViewModel? item)
    {
        if (item == null || !File.Exists(item.SavePath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(item.SavePath) { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenFolder(DownloadItemViewModel? item)
    {
        if (item == null)
        {
            return;
        }

        string? folder = Path.GetDirectoryName(item.SavePath);
        if (folder == null || !Directory.Exists(folder))
        {
            return;
        }

        Process.Start("explorer.exe", $"/select,\"{item.SavePath}\"");
    }

    [RelayCommand]
    private void OpenActionFlyout()
    {
        if (!IsFlyoutOpen)
        {
            IsFlyoutOpen = true;
        }
    }

    [RelayCommand]
    private async Task DeleteAllCompleted()
    {
        IsFlyoutOpen = false;

        Dialogs.ViewModels.Messages? result = await MessengerService.ShowDialogAsync<Dialogs.Views.Messages, Dialogs.ViewModels.Messages, Dialogs.Models.Messages>(new Dialogs.Models.Messages
        {
            MessageButtonType = Dialogs.Models.Messages.MessageButton.YesNo,
            MessageImageType = Dialogs.Models.Messages.MessageImage.Warning,
            MessageTitleKey = "dialog_warn_title",
            MessageContentKey = "page_downloads_dialog_delete_allcpl_summary"
        });

        if (result?.MessageResult == Dialogs.Models.Messages.MessageResult.Yes)
        {
            ConfluxManager.cfsPDownloaderCore?.Send(
                DownloadProtocol.RunnerClear,
                DownloadClearScope.Completed);
        }
    }

    [RelayCommand]
    private async Task DeleteAll()
    {
        IsFlyoutOpen = false;

        Dialogs.ViewModels.Messages? result = await MessengerService.ShowDialogAsync<Dialogs.Views.Messages, Dialogs.ViewModels.Messages, Dialogs.Models.Messages>(new Dialogs.Models.Messages
        {
            MessageButtonType = Dialogs.Models.Messages.MessageButton.YesNo,
            MessageImageType = Dialogs.Models.Messages.MessageImage.Warning,
            MessageTitleKey = "dialog_warn_title",
            MessageContentKey = "page_downloads_dialog_delete_all_summary"
        });

        if (result?.MessageResult == Dialogs.Models.Messages.MessageResult.Yes)
        {
            ConfluxManager.cfsPDownloaderCore?.Send(
                DownloadProtocol.RunnerClear,
                DownloadClearScope.All);
        }
    }

    [RelayCommand]
    private async Task PauseAll()
    {
        IsFlyoutOpen = false;

        Dialogs.ViewModels.Messages? result = await MessengerService.ShowDialogAsync<Dialogs.Views.Messages, Dialogs.ViewModels.Messages, Dialogs.Models.Messages>(new Dialogs.Models.Messages
        {
            MessageButtonType = Dialogs.Models.Messages.MessageButton.YesNo,
            MessageImageType = Dialogs.Models.Messages.MessageImage.Warning,
            MessageTitleKey = "dialog_warn_title",
            MessageContentKey = "page_downloads_dialog_pause_all_summary"
        });

        if (result?.MessageResult == Dialogs.Models.Messages.MessageResult.Yes)
        {
            ConfluxManager.cfsPDownloaderCore?.Send(DownloadProtocol.RunnerPauseAll);
        }
    }

    [RelayCommand]
    private async Task ResumeAll()
    {
        IsFlyoutOpen = false;

        Dialogs.ViewModels.Messages? result = await MessengerService.ShowDialogAsync<Dialogs.Views.Messages, Dialogs.ViewModels.Messages, Dialogs.Models.Messages>(new Dialogs.Models.Messages
        {
            MessageButtonType = Dialogs.Models.Messages.MessageButton.YesNo,
            MessageImageType = Dialogs.Models.Messages.MessageImage.Warning,
            MessageTitleKey = "dialog_warn_title",
            MessageContentKey = "page_downloads_dialog_pause_all_summary"
        });

        if (result?.MessageResult == Dialogs.Models.Messages.MessageResult.Yes)
        {
            ConfluxManager.cfsPDownloaderCore?.Send(DownloadProtocol.RunnerResumeAll);
        }
    }

    [RelayCommand]
    private async Task RetryAll()
    {
        IsFlyoutOpen = false;

        Dialogs.ViewModels.Messages? result = await MessengerService.ShowDialogAsync<Dialogs.Views.Messages, Dialogs.ViewModels.Messages, Dialogs.Models.Messages>(new Dialogs.Models.Messages
        {
            MessageButtonType = Dialogs.Models.Messages.MessageButton.YesNo,
            MessageImageType = Dialogs.Models.Messages.MessageImage.Warning,
            MessageTitleKey = "dialog_warn_title",
            MessageContentKey = "page_downloads_dialog_retry_all_summary"
        });

        if (result?.MessageResult == Dialogs.Models.Messages.MessageResult.Yes)
        {
            ConfluxManager.cfsPDownloaderCore?.Send(DownloadProtocol.RunnerRetryAll);
        }
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
    private void Refresh()
    {
        RequestRefresh();
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
    }
}
