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

namespace PDownloader.ViewModels.Windows;

public partial class MainWindowViewModel : ObservableObject
{
    private bool _isInitialized = false;

    public void OnNavigatedTo()
    {
        if (!_isInitialized)
        {
            InitializeViewModel();
        }
    }

    private void InitializeViewModel()
    {
        _isInitialized = true;
    }

    [ObservableProperty]
    private string _applicationTitle = "PDownloader";

    [ObservableProperty]
    private ObservableCollection<object> _menuItems;

    [ObservableProperty]
    private ObservableCollection<object> _footerMenuItems;

    public MainWindowViewModel(INavigationService navigationService, UpdateHostService updateHostService)
    {
        NavigationHandle.NavigationService = navigationService;
        _menuItems = NavigationHandle.GetNavCardsInNamespace("PDownloader.Views.Pages");
        _footerMenuItems = NavigationHandle.GetNavCardsInNamespace("PDownloader.Views.PagesBottom");

        LanguageBase.LanguageChanged += (lang) =>
        {
            _ = ConfluxManager.cfsPDownloaderCore?.SendAsync(
                AppProtocol.MainEventMessage,
                AppProtocol.MainEvent.LanguageChanged);
        };

        _ = updateHostService.CheckAsync(release =>
        {
            Application.Current.Dispatcher.Invoke(() =>
                MessengerService.ShowSnackbar("sys_notification_title", LanguageBase.GetLangValue("update_available_summary", release.TagName), ControlAppearance.Caution, new SymbolIcon(SymbolRegular.ArrowDownload24), TimeSpan.FromSeconds(15)));
        });
    }
}
