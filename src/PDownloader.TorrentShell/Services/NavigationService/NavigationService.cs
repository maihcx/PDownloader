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

namespace PDownloader.TorrentShell.Services;

public sealed class NavigationService : INavigationService
{
    private readonly IServiceProvider _serviceProvider;

    public NavigationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void NavigateTo(Type pageType)
    {
        if (!typeof(UIElement).IsAssignableFrom(pageType))
        {
            throw new ArgumentException($"{pageType.Name} must inherit UIElement.");
        }

        IWindow window = _serviceProvider.GetRequiredService<IWindow>();
        UIElement page = (UIElement)_serviceProvider.GetRequiredService(pageType);
        window.FrameHost.Navigate(page);
    }
}
