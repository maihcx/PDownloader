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

global using CommunityToolkit.Mvvm.ComponentModel;
global using CommunityToolkit.Mvvm.Input;
global using PDownloader.CFS;
global using PDownloader.Contracts.Updates;
global using PDownloader.Tray.Models;
global using PDownloader.Tray.Resources;
global using PDownloader.Tray.Services;
global using PDownloader.Tray.Utils;
global using PDownloader.Tray.ViewModels;
global using PDownloader.Tray.Views;
global using System.Collections.ObjectModel;
global using System.ComponentModel;
global using System.Globalization;
global using System.IO;
global using System.Resources;
global using System.Text.Json;
global using System.Windows;
global using System.Windows.Data;
global using Wpf.Ui.Abstractions.Controls;
global using Wpf.Ui.Appearance;
global using Wpf.Ui.Controls;
global using ThemeType = Wpf.Ui.Appearance.ApplicationTheme;
global using Watcher = Wpf.Ui.Appearance.SystemThemeWatcher;
