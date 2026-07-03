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

namespace PDownloader.ViewModels.Pages;

public partial class ConfigViewModel : ObservableObject
{
    private readonly DownloadConfigService _configService;
    private readonly DownloadLauncherService _launcher;

    [ObservableProperty]
    private DownloadConfigs? _downloadConfigs;

    [ObservableProperty]
    private bool _isDaemonRunning;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ConfigViewModel(DownloadLauncherService launcher, DownloadConfigService configService)
    {
        _configService = configService;
        _downloadConfigs = configService.DownloadConfigs;
        _launcher = launcher;

        IsDaemonRunning = _launcher.IsDaemonRunning;
        StatusMessage = IsDaemonRunning
            ? LanguageBase.GetLangValue("page_config_svc_active_title")
            : LanguageBase.GetLangValue("page_config_svc_inactive_title");
    }

    [RelayCommand]
    private void BrowseDownloadFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = LanguageBase.GetLangValue("page_config_folder_title"),
            InitialDirectory = DownloadConfigs?.DefaultDownloadFolder,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            DownloadConfigs?.DefaultDownloadFolder = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        _configService.Save();

        _launcher.RefreshConfigs();
        StatusMessage = LanguageBase.GetLangValue("page_config_save") + " ✓";
    }
}
