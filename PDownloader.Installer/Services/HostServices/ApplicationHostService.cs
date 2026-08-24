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

namespace PDownloader.Installer.Services.HostServices;

public sealed class ApplicationHostService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly InstallerLaunchOptions _launchOptions;
    private readonly IInstallService _installService;
    private readonly IInstallerApplicationService _applicationService;

    public ApplicationHostService(
        IServiceProvider serviceProvider,
        InstallerLaunchOptions launchOptions,
        IInstallService installService,
        IInstallerApplicationService applicationService)
    {
        _serviceProvider = serviceProvider;
        _launchOptions = launchOptions;
        _installService = installService;
        _applicationService = applicationService;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_launchOptions.IsSilentMode)
        {
            // A silent run has no window, so keep WPF alive until the operation
            // explicitly shuts down with a success or failure exit code.
            System.Windows.Application.Current.ShutdownMode =
                System.Windows.ShutdownMode.OnExplicitShutdown;

            _ = RunSilentAsync(cancellationToken);
            return Task.CompletedTask;
        }

        _serviceProvider.GetRequiredService<IWindow>().Show();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;

    private async Task RunSilentAsync(CancellationToken cancellationToken)
    {
        int exitCode = 0;

        try
        {
            var progress = new Progress<(double Percent, string Status)>(_ => { });

            if (_launchOptions.IsUninstallMode)
            {
                string uninstallDirectory = _launchOptions.InstallDirectory
                    ?? _installService.GetInstalledDir()
                    ?? _installService.DefaultInstallPath;

                await _installService.UninstallAsync(
                    Path.GetFullPath(uninstallDirectory),
                    progress,
                    cancellationToken);
            }
            else
            {
                string installDirectory = Path.GetFullPath(
                    _launchOptions.InstallDirectory
                    ?? _installService.GetInstalledDir()
                    ?? _installService.DefaultInstallPath);

                bool runAtStartup = _launchOptions.RunAtStartup
                    ?? UserDataStore.GetValue<bool>("IsStartAtBoot");

                await _installService.InstallAsync(
                    installDirectory,
                    _launchOptions.DesktopShortcut,
                    _launchOptions.StartMenuShortcut,
                    _launchOptions.InstallBrowserExtension,
                    runAtStartup,
                    progress,
                    cancellationToken);

                if (_launchOptions.LaunchAfterInstall)
                {
                    _applicationService.TryLaunch(
                        Path.Combine(installDirectory, "PDownloader.exe"),
                        installDirectory);
                }
            }
        }
        catch
        {
            exitCode = 1;
        }

        _applicationService.Shutdown(exitCode);
    }
}
