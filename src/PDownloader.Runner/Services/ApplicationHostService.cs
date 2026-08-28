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
/// Owns creation and presentation of the WPF shell. This service is deliberately
/// not an IHostedService: Generic Host does not guarantee that hosted-service
/// continuations run on the WPF Dispatcher/STA thread.
/// </summary>
public sealed class ApplicationHostService
{
    private readonly IServiceProvider _serviceProvider;
    private IWindow? _mainWindow;
    private bool _shown;

    public ApplicationHostService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Creates, initializes and shows the main Runner window on the WPF Dispatcher.
    /// </summary>
    public async Task ShowAsync(CancellationToken cancellationToken = default)
    {
        if (_shown)
        {
            return;
        }

        Application application = Application.Current
            ?? throw new InvalidOperationException(
                "WPF Application is not available while starting Runner UI.");

        if (!application.Dispatcher.CheckAccess())
        {
            await application.Dispatcher
                .InvokeAsync(
                    () => ShowCoreAsync(cancellationToken),
                    System.Windows.Threading.DispatcherPriority.Normal,
                    cancellationToken)
                .Task
                .Unwrap();
            return;
        }

        await ShowCoreAsync(cancellationToken);
    }

    private async Task ShowCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_shown)
        {
            return;
        }

        _mainWindow = _serviceProvider.GetRequiredService<IWindow>();

        if (_mainWindow is MainWindow window
            && window.ViewModel is INavigationAware navigationAware)
        {
            await navigationAware.OnNavigatedToAsync();
        }

        cancellationToken.ThrowIfCancellationRequested();

        _mainWindow.Loaded += MainWindow_Loaded;
        _mainWindow.Show();
        _shown = true;
    }

    private static void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Application? application = Application.Current;
        if (application?.MainWindow is not Window mainWindow)
        {
            return;
        }

        mainWindow.Activate();
        mainWindow.Topmost = false;
    }
}
