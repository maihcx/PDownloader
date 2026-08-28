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

public partial class DownloaderViewModel : ObservableObject
{
    private bool _isInitialized = false;

    private Services.INavigationService _navigationService { get; set; }

    private readonly DownloaderService _downloaderService;

    [ObservableProperty]
    private RunnerConfig _runnerConfig;

    [ObservableProperty]
    private DownloaderServiceStatus _downloaderStatus;

    public DownloaderViewModel(Services.INavigationService navigationService, RunnerConfig runnerConfig, DownloaderService downloaderService)
    {
        _navigationService = navigationService;
        RunnerConfig = runnerConfig;
        _downloaderService = downloaderService;
        _downloaderStatus = downloaderService.DownloaderStatus;

        if (!_isInitialized)
        {
            InitializeViewModel();
        }
    }

    private void InitializeViewModel()
    {
        _isInitialized = true;
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var dlg = new OpenFolderDialog
        {
            Title = LanguageBase.GetLangValue("select_folder_title"),
            InitialDirectory = RunnerConfig.SaveTo
        };
        if (dlg.ShowDialog() == true)
        {
            RunnerConfig.SaveTo = dlg.FolderName;
        }
    }

    [RelayCommand]
    private void CancelDownload()
    {
        Application.Current.Shutdown();
    }

    [RelayCommand]
    private async Task ConfirmDownload()
    {
        DownloaderServiceStatus status = await _downloaderService.StartDownload();

        if (status.State == RunnerState.Downloading)
        {
            _navigationService.NavigateTo(typeof(DownloaderProgressPage));
        }
    }
}
