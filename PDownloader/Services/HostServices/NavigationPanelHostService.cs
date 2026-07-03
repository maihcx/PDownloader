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

namespace PDownloader.Services.HostServices;

public partial class NavigationPanelHostService : ObservableObject, IHostedService
{
    private readonly IServiceProvider _serviceProvider;

    private readonly INavigationService _navigationService;

    private INavigationView? _navigationView;

    private IWindow? mainWindow = null;

    private readonly int _maxWindowsWidth = 900;

    private PowerModeService? _powerModeService;

    [ObservableProperty]
    public NaviPanelOpenState _naviPanelOpen;

    public NavigationPanelHostService(IServiceProvider serviceProvider, INavigationService navigationService)
    {
        _serviceProvider = serviceProvider;
        _navigationService = navigationService;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await HandleNavAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            mainWindow?.SizeChanged -= MainWindow_SizeChanged;
        }
        catch { }

        await Task.CompletedTask;
    }

    public bool GetIsPanelInternalOpen()
    {
        return UserDataStore.GetValue<bool>("IsNavPaneOpen");
    }

    private NaviPanelOpenState GetNavOpenState()
    {
        if (UserDataStore.GetValue<bool>("IsAutoHideNavPanel"))
        {
            return NaviPanelOpenState.Auto;
        }
        else if (UserDataStore.GetValue<bool>("IsNavPaneOpen"))
        {
            return NaviPanelOpenState.Open;
        }

        return NaviPanelOpenState.Close;
    }

    private Task HandleNavAsync()
    {
        mainWindow = _serviceProvider.GetRequiredService<IWindow>();

        _navigationView = _navigationService.GetNavigationControl();

        _powerModeService = _serviceProvider.GetRequiredService<PowerModeService>();

        mainWindow.SizeChanged += MainWindow_SizeChanged;

        _navigationView.PaneOpened += _navigationView_PaneOpened;

        _navigationView.PaneClosed += _navigationView_PaneClosed;

        _navigationView.Navigated += _navigationView_Navigated;

        NaviPanelOpen = GetNavOpenState();

        return Task.CompletedTask;
    }

    private void _navigationView_Navigated(NavigationView sender, NavigatedEventArgs args)
    {
        if (args?.Page is not FrameworkElement page)
        {
            return;
        }

        Type pageType = page.GetType();

        var metaAttr = pageType.GetCustomAttributes(typeof(PageMetaAttribute), true)
                               .FirstOrDefault() as PageMetaAttribute;

        if (metaAttr != null && metaAttr.IsShowPageTitle)
        {
            mainWindow?.BreadcrumbBar?.Visibility = Visibility.Visible;
            mainWindow?.BreadcrumbBarHolder.Visibility = Visibility.Collapsed;
        }
        else
        {
            mainWindow?.BreadcrumbBar.Visibility = Visibility.Collapsed;
            mainWindow?.BreadcrumbBarHolder.Visibility = Visibility.Visible;
        }

        _ = _powerModeService?.OptimizeAfterAsync(TimeSpan.FromSeconds(2));
    }

    private void _navigationView_PaneClosed(NavigationView sender, RoutedEventArgs args)
    {
        if (NaviPanelOpen != NaviPanelOpenState.Auto)
        {
            NaviPanelOpen = NaviPanelOpenState.Close;
        }
    }

    private void _navigationView_PaneOpened(NavigationView sender, RoutedEventArgs args)
    {
        if (NaviPanelOpen != NaviPanelOpenState.Auto)
        {
            NaviPanelOpen = NaviPanelOpenState.Open;
        }
    }

    private void MainWindow_SizeChanged(object? sender, SizeChangedEventArgs? e)
    {
        if (this.NaviPanelOpen != NaviPanelOpenState.Auto)
        {
            return;
        }

        double size_width = mainWindow?.Width ?? 0;
        if (size_width < _maxWindowsWidth && (_navigationView?.IsPaneOpen ?? false))
        {
            _navigationView?.IsPaneOpen = false;
        }
        else if (size_width >= _maxWindowsWidth && !(_navigationView?.IsPaneOpen ?? true))
        {
            _navigationView?.IsPaneOpen = true;
        }
    }

    partial void OnNaviPanelOpenChanged(NaviPanelOpenState value)
    {
        bool isPanelOpen = false;

        if (value == NaviPanelOpenState.Auto)
        {
            UserDataStore.SetValue("IsAutoHideNavPanel", true);
            this.MainWindow_SizeChanged(null, null);
            return;
        }
        else if (value == NaviPanelOpenState.Open)
        {
            isPanelOpen = true;
            UserDataStore.SetValue("IsNavPaneOpen", true);
        }
        else
        {
            UserDataStore.SetValue("IsNavPaneOpen", false);
        }

        UserDataStore.SetValue("IsAutoHideNavPanel", false);

        _navigationView?.IsPaneOpen = isPanelOpen;
    }
}
