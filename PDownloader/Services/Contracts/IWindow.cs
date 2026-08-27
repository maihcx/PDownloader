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

namespace PDownloader.Services.Contracts;

public interface IWindow
{
    event RoutedEventHandler Loaded;

    event SizeChangedEventHandler SizeChanged;

    event EventHandler Activated;

    event EventHandler Deactivated;

    event EventHandler StateChanged;

    WindowState WindowState { get; }

    BreadcrumbBar BreadcrumbBar { get; }

    BreadcrumbBar BreadcrumbBarHolder { get; }

    double Width { get; }

    void Show();
}
