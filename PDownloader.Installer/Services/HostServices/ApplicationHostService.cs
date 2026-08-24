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
            InstallerPreferences preferences = InstallerPreferencesStore.Load();
            string language = InstallerPreferencesStore.NormalizeLanguage(
                _launchOptions.RequestedLanguage ?? preferences.Language);
            LanguageBase.SetLanguage(language);

            InstallScope? existingInstallScope = _launchOptions.IsUninstallMode
                ? null
                : GetExistingInstallScope(_launchOptions.RequestedInstallScope);
            InstallScope installScope = _launchOptions.IsUninstallMode
                ? _launchOptions.RequestedInstallScope
                    ?? _installService.GetInstalledScope()
                    ?? preferences.InstallScope
                : existingInstallScope
                    ?? _launchOptions.RequestedInstallScope
                    ?? preferences.InstallScope;
            string? existingInstallDirectory = existingInstallScope.HasValue
                ? _installService.GetInstalledDir(existingInstallScope.Value)
                : null;
            string? registeredUninstallDirectory = _launchOptions.IsUninstallMode
                ? _installService.GetInstalledDir(installScope)
                : null;
            bool desktopShortcut = _launchOptions.DesktopShortcut
                ?? preferences.DesktopShortcut;
            bool startMenuShortcut = _launchOptions.StartMenuShortcut
                ?? preferences.StartMenuShortcut;
            bool installBrowserExtension = _launchOptions.InstallBrowserExtension
                ?? preferences.InstallBrowserExtension;
            bool runAtStartup = _launchOptions.RunAtStartup
                ?? preferences.RunAtStartup;

            InstallerLaunchOptions resolvedOptions = _launchOptions with
            {
                RequestedInstallScope = installScope,
                RequestedLanguage = language,
                InstallDirectory = existingInstallDirectory
                    ?? registeredUninstallDirectory
                    ?? _launchOptions.InstallDirectory,
                DesktopShortcut = desktopShortcut,
                StartMenuShortcut = startMenuShortcut,
                InstallBrowserExtension = installBrowserExtension,
                RunAtStartup = runAtStartup,
            };

            if (installScope == InstallScope.AllUsers
                && !_applicationService.IsAdministrator)
            {
                InstallerLaunchOptions elevatedOptions = resolvedOptions with
                {
                    IsSilentMode = true,
                };

                int? elevatedExitCode = await _applicationService.RunElevatedAsync(
                    elevatedOptions.ToArguments(),
                    cancellationToken);

                if (elevatedExitCode == 0 && !_launchOptions.IsUninstallMode)
                {
                    SavePreferences(resolvedOptions, installScope);
                }

                _applicationService.Shutdown(elevatedExitCode ?? 1);
                return;
            }

            if (_launchOptions.IsUninstallMode)
            {
                string uninstallDirectory = resolvedOptions.InstallDirectory
                    ?? _installService.GetInstalledDir(installScope)
                    ?? _installService.GetDefaultInstallPath(installScope);

                await _installService.UninstallAsync(
                    Path.GetFullPath(uninstallDirectory),
                    installScope,
                    progress,
                    cancellationToken);
            }
            else
            {
                string installDirectory = Path.GetFullPath(
                    resolvedOptions.InstallDirectory
                    ?? _installService.GetInstalledDir(installScope)
                    ?? _installService.GetDefaultInstallPath(installScope));

                await _installService.InstallAsync(
                    installDirectory,
                    installScope,
                    desktopShortcut,
                    startMenuShortcut,
                    installBrowserExtension,
                    runAtStartup,
                    progress,
                    cancellationToken);

                SavePreferences(resolvedOptions, installScope);

                if (resolvedOptions.LaunchAfterInstall)
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

    private static void SavePreferences(
        InstallerLaunchOptions options,
        InstallScope installScope)
    {
        InstallerPreferencesStore.Save(new InstallerPreferences
        {
            InstallScope = installScope,
            Language = options.RequestedLanguage ?? "en",
            DesktopShortcut = options.DesktopShortcut ?? true,
            StartMenuShortcut = options.StartMenuShortcut ?? true,
            InstallBrowserExtension = options.InstallBrowserExtension ?? true,
            RunAtStartup = options.RunAtStartup ?? false,
        });
    }

    private InstallScope? GetExistingInstallScope(InstallScope? preferredScope)
    {
        if (preferredScope.HasValue
            && !string.IsNullOrWhiteSpace(
                _installService.GetInstalledDir(preferredScope.Value)))
        {
            return preferredScope.Value;
        }

        return _installService.GetInstalledScope();
    }
}
