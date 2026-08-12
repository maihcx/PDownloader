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

    public ObservableCollection<DownloadItemDto> Downloads { get; } = new();

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

    private readonly DownloadLauncherService _downloadLauncherService;

    public DownloadsViewModel(
        DownloadsChannelService downloadsChannelService,
        DownloadLauncherService downloadLauncherService)
    {
        downloadsChannelService.OnProgress += OnProgress;
        downloadsChannelService.OnList += OnList;

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

            ConfluxManager.cfsPDownloaderCore?.Send(
                "downloader-svc-getlist",
                string.Empty);

            _isInitialized = true;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        DownloadsView.Refresh();
        UpdateViewState();
    }

    private bool FilterDownload(object item)
    {
        if (item is not DownloadItemDto download)
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
                        nameof(DownloadItemDto.FileName),
                        ListSortDirection.Ascending);
                    break;

                case DownloadSortMode.NameDescending:
                    AddSort(
                        nameof(DownloadItemDto.FileName),
                        ListSortDirection.Descending);
                    break;

                case DownloadSortMode.TimeStartAscending:
                    AddSort(
                        nameof(DownloadItemDto.StartTime),
                        ListSortDirection.Ascending);
                    break;

                case DownloadSortMode.TimeStartDescending:
                    AddSort(
                        nameof(DownloadItemDto.StartTime),
                        ListSortDirection.Descending);
                    break;

                case DownloadSortMode.TimeEndAscending:
                    AddSort(
                        nameof(DownloadItemDto.EndTime),
                        ListSortDirection.Ascending);
                    break;

                case DownloadSortMode.TimeEndDescending:
                    AddSort(
                        nameof(DownloadItemDto.EndTime),
                        ListSortDirection.Descending);
                    break;

                case DownloadSortMode.SizeAscending:
                    AddSort(
                        nameof(DownloadItemDto.TotalBytes),
                        ListSortDirection.Ascending);
                    break;

                case DownloadSortMode.SizeDescending:
                    AddSort(
                        nameof(DownloadItemDto.TotalBytes),
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

    private void OnList(List<DownloadItemDto> items)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            Downloads.Clear();

            foreach (DownloadItemDto item in items)
            {
                Downloads.Add(item);
            }

            IsLoading = false;

            RefreshFilteredView();
        });
    }

    private void OnProgress(DownloadItemDto dto)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            DownloadItemDto? existing = Downloads.FirstOrDefault(d => d.Id == dto.Id);
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
    private void Pause(DownloadItemDto? item)
    {
        if (item == null || !item.CanPause)
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

        if (item.StatusState == DownloadStatus.Completed)
        {
            OpenFile(item);
        }
        else if (item.StatusState == DownloadStatus.Paused && item.CanResume)
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

        string? folder = Path.GetDirectoryName(item.SavePath);
        if (folder == null || !Directory.Exists(folder))
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
