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
global using PDownloader.ControlsLookup;
global using PDownloader.Models;
global using PDownloader.Resources;
global using PDownloader.Services;
global using PDownloader.Services.Contracts;
global using PDownloader.Services.DownloadServices;
global using PDownloader.Services.HostServices;
global using PDownloader.Services.UpdateServices;
global using PDownloader.Utils;
global using PDownloader.ViewModels.Pages;
global using PDownloader.ViewModels.PagesBottom;
global using PDownloader.ViewModels.Windows;
global using PDownloader.Views.Pages;
global using PDownloader.Views.PagesBottom;
global using PDownloader.Views.Windows;
global using System;
global using System.Collections.ObjectModel;
global using System.ComponentModel;
global using System.Diagnostics;
global using System.Globalization;
global using System.IO;
global using System.IO.Pipes;
global using System.Reflection;
global using System.Resources;
global using System.Runtime.CompilerServices;
global using System.Runtime.InteropServices;
global using System.Security.AccessControl;
global using System.Security.Cryptography;
global using System.Security.Principal;
global using System.Text;
global using System.Text.Json;
global using System.Windows;
global using System.Windows.Controls;
global using System.Windows.Controls.Primitives;
global using System.Windows.Data;
global using System.Windows.Interop;
global using System.Windows.Media;
global using System.Windows.Media.Animation;
global using System.Windows.Threading;
global using Wpf.Ui;
global using Wpf.Ui.Abstractions.Controls;
global using Wpf.Ui.Appearance;
global using Wpf.Ui.Controls;
global using Wpf.Ui.DependencyInjection;
global using Binding = System.Windows.Data.Binding;
global using Control = System.Windows.Controls.Control;
global using IRelayCommand = Wpf.Ui.Input.IRelayCommand;
global using ThemeType = Wpf.Ui.Appearance.ApplicationTheme;
global using Watcher = Wpf.Ui.Appearance.SystemThemeWatcher;
