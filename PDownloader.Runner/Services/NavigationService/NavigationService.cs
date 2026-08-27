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

namespace PDownloader.Runner.Services;

/// <summary>
/// Managed host of the application.
/// </summary>
public class NavigationService : INavigationService
{
    private readonly IServiceProvider? _serviceProvider;

    private readonly PowerModeService _powerModeService;

    private IWindow? _mainWindow;

    public NavigationService(IServiceProvider serviceProvider, PowerModeService powerModeService)
    {
        _serviceProvider = serviceProvider;
        _powerModeService = powerModeService;
    }

    public void NavigateTo(Type pageType)
    {
        _mainWindow = (
            _serviceProvider?.GetService(typeof(IWindow)) as IWindow
        )!;

        if (!typeof(UIElement).IsAssignableFrom(pageType))
        {
            throw new ArgumentException($"{pageType.Name} must inherit UIElement.");
        }

        if (_serviceProvider == null)
        {
            throw new Exception("serviceProvider not available.");
        }

        var page = (UIElement)_serviceProvider.GetRequiredService(pageType);

        _mainWindow.FrameHost.Navigate(page);

        _ = _powerModeService.OptimizeAfterAsync(TimeSpan.FromSeconds(2));
    }
}
