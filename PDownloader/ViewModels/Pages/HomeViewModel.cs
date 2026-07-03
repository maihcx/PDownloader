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

public partial class HomeViewModel : ObservableObject
{
    private bool _isInitialized = false;

    [ObservableProperty]
    private ICollection<NavigationCard> _navigationCards = NavigationHandle.GetNavigationCards(["PDownloader.Views.Pages", "PDownloader.Views.PagesBottom"], typeof(HomePage));

    public Task OnNavigatedToAsync()
    {
        if (!_isInitialized)
        {
            InitializeViewModel();
        }

        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;

    private void InitializeViewModel()
    {
        _isInitialized = true;
    }

    [ObservableProperty]
    private string _appName = AppInfoHelper.AppName;

    [ObservableProperty]
    private string _author = AppInfoHelper.Author;

    [ObservableProperty]
    private string _sortAuthor = AppInfoHelper.SortAuthor;

    [ObservableProperty]
    private string _authorCreated = AppInfoHelper.AuthorCreated;

    [ObservableProperty]
    private string _appDescription = AppInfoHelper.AppDescription;
}
