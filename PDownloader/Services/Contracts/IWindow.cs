// This Source Code Form is subject to the terms of the MIT License.
// If a copy of the MIT was not distributed with this file, You can obtain one at https://opensource.org/licenses/MIT.
// Copyright (C) Leszek Pomianowski and WPF UI Contributors.
// All Rights Reserved.

namespace PDownloader.Services.Contracts;

public interface IWindow
{
    event RoutedEventHandler Loaded;

    event SizeChangedEventHandler SizeChanged;

    event EventHandler Activated;

    event EventHandler Deactivated;

    event EventHandler StateChanged;

    WindowState WindowState { get; }

    double Width { get; }

    void Show();
}
