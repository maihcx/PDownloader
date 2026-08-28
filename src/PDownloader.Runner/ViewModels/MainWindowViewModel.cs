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

public partial class MainWindowViewModel : ObservableObject, Services.INavigationAware
{
    private bool _isInitialized = false;

    private Services.INavigationService _navigationService { get; set; }

    private RunnerConfig _runnerConfig { get; set; }

    [ObservableProperty]
    private string _applicationTitle = "PDownloader";

    public MainWindowViewModel(Services.INavigationService navigationService, RunnerConfig runnerConfig)
    {
        _navigationService = navigationService;
        _runnerConfig = runnerConfig;

        if (!_isInitialized)
        {
            InitializeViewModel();
        }
    }

    private void InitializeViewModel()
    {
        _isInitialized = true;
    }

    public Task OnNavigatedToAsync()
    {
        if (_runnerConfig.IsArgsSetup)
        {
            if (_runnerConfig.IsRunner)
            {
                _navigationService.NavigateTo(typeof(DownloaderProgressPage));
            }
            else
            {
                _navigationService.NavigateTo(typeof(DownloaderPage));
            }
        }

        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync()
    {
        return Task.CompletedTask;
    }
}
