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
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Win32;
global using PDownloader.CFS;
global using PDownloader.Contracts.Application;
global using PDownloader.Contracts.Downloads;
global using PDownloader.Contracts.Ipc;
//global using PDownloader.Runner.Views.Windows;
//global using PDownloader.Runner.Views.Pages;
//global using PDownloader.Runner.Views.PagesBottom;
//global using PDownloader.Runner.ControlsLookup;
global using PDownloader.Runner.Models;
global using PDownloader.Runner.Services;
global using PDownloader.Runner.Services.Contracts;
global using PDownloader.Runner.Utils;
//global using PDownloader.Runner.Resources;
global using PDownloader.Runner.ViewModels.Windows;
//global using PDownloader.Runner.ViewModels.Pages;
//global using PDownloader.Runner.ViewModels.PagesBottom;
global using PDownloader.Runner.Views;
global using System;
global using System.Collections.ObjectModel;
global using System.ComponentModel;
global using System.Diagnostics;
global using System.Globalization;
global using System.IO;
global using System.Resources;
global using System.Runtime.CompilerServices;
global using System.Runtime.InteropServices;
global using System.Text;
global using System.Text.Json;
global using System.Windows;
global using System.Windows.Controls;
global using System.Windows.Data;
global using System.Windows.Media;
global using System.Windows.Threading;
global using Wpf.Ui.Abstractions.Controls;
global using Wpf.Ui.Appearance;
global using Wpf.Ui.Controls;
global using Binding = System.Windows.Data.Binding;
global using DownloadThreadProgressDto = PDownloader.Contracts.Downloads.DownloadThreadProgress;
global using ThemeType = Wpf.Ui.Appearance.ApplicationTheme;
global using Watcher = Wpf.Ui.Appearance.SystemThemeWatcher;
