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

namespace PDownloader.TorrentSel;

public partial class App
{
    private static IHost? _host;

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        try
        {
            await UserDataStore.InitializeAsync();
            TranslationSource.Instance.CurrentCulture = LanguageBase.GetSetupLanguage();
            _host = CreateHost();
            await _host.StartAsync();
            ApplicationThemeManager.ApplySystemTheme();
            MainWindow window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
            _host.Services.GetRequiredService<TorrentSelectionService>().SetReady(true);
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                exception.Message,
                LanguageBase.GetLangValue("torrent_selector_app_title"),
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private async void OnExit(object sender, ExitEventArgs e)
    {
        if (_host is null)
        {
            return;
        }

        try { await _host.StopAsync(TimeSpan.FromSeconds(3)); }
        finally { _host.Dispose(); }
    }

    private static IHost CreateHost() => Microsoft.Extensions.Hosting.Host
        .CreateDefaultBuilder()
        .ConfigureServices(services =>
        {
            services.AddSingleton(TorrentSelectorConfig.Parse(
                Environment.GetCommandLineArgs().Skip(1).ToArray()));
            services.AddSingleton<TorrentSelectionService>();
            services.AddHostedService(service =>
                service.GetRequiredService<TorrentSelectionService>());
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<MainWindow>();
        })
        .Build();
}
