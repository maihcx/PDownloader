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

public partial class MainWindowViewModel : ObservableObject, Services.INavigationAware
{
    private readonly INavigationService _navigationService;
    private readonly TorrentShellConfig _torrentConfig;

    [ObservableProperty]
    private string _applicationTitle = LanguageBase.GetLangValue("torrent_shell_app_title");

    public MainWindowViewModel(
        INavigationService navigationService,
        TorrentShellConfig torrentConfig)
    {
        _navigationService = navigationService;
        _torrentConfig = torrentConfig;
    }

    public Task OnNavigatedToAsync()
    {
        _navigationService.NavigateTo(_torrentConfig.HasStarted
            ? typeof(DownloaderProgressPage)
            : typeof(DownloaderPage));
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;
}
