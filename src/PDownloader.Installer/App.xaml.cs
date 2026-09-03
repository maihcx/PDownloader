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

using System.Windows;

namespace PDownloader.Installer;

public partial class App
{
    private readonly IHost _host;
    private readonly InstallerLaunchOptions _launchOptions;

    public App()
    {
        _launchOptions = InstallerLaunchOptions.Parse(
            Environment.GetCommandLineArgs().Skip(1));

        _host = Host
            .CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton(_launchOptions);

                services.AddSingleton<IInstallService, InstallService>();
                services.AddSingleton<ILicenseService, LicenseService>();
                services.AddSingleton<IFolderPickerService, FolderPickerService>();
                services.AddSingleton<IInstallerApplicationService, InstallerApplicationService>();

                services.AddSingleton<InstallerViewModel>();
                services.AddSingleton<IWindow, MainWindow>();
                services.AddHostedService<ApplicationHostService>();
            })
            .Build();
    }

    public static IServiceProvider Services =>
        ((App)Current)._host.Services;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        try
        {
            UserDataStore.Reload();
            // Keep window creation on the WPF dispatcher, as before.
            _host.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            if (!_launchOptions.IsSilentMode)
            {
                System.Windows.MessageBox.Show(ex.Message, "PDownloader settings",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }

            Shutdown(1);
        }
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        _host.Services
            .GetRequiredService<IInstallService>()
            .ScheduleTemporaryFilesCleanup(_launchOptions.UpdateTempDirectory);

        _host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        _host.Dispose();
    }

    public static T GetRequiredService<T>()
        where T : class
    {
        return Services.GetRequiredService<T>();
    }
}
