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

    public ObservableCollection<FileMergeModeOption> FileMergeModeOptions { get; } =
    [
        new(
            "HighPerformance",
            "page_config_merge_mode_high_performance_title",
            "page_config_merge_mode_high_performance_description"),
        new(
            "Balanced",
            "page_config_merge_mode_balanced_title",
            "page_config_merge_mode_balanced_description"),
        new(
            "DataIntegrity",
            "page_config_merge_mode_data_integrity_title",
            "page_config_merge_mode_data_integrity_description")
    ];

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
            DownloadConfigs!.DefaultDownloadFolder = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void BrowseTempFolder()
    {
        string configuredFolder = DownloadConfigs?.DefaultTempFolder ?? string.Empty;
        string initialDirectory = Directory.Exists(configuredFolder)
            ? configuredFolder
            : DownloadConfigService.GetDefaultTempFolder();

        var dialog = new OpenFolderDialog
        {
            Title = LanguageBase.GetLangValue("page_config_temp_folder_title"),
            InitialDirectory = initialDirectory,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            DownloadConfigs!.DefaultTempFolder = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void ResetTempFolder()
    {
        if (DownloadConfigs != null)
        {
            DownloadConfigs.DefaultTempFolder = DownloadConfigService.GetDefaultTempFolder();
        }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        if (!_configService.TrySave(out string errorMessage))
        {
            StatusMessage = string.Format(
                CultureInfo.CurrentCulture,
                LanguageBase.GetLangValue("page_config_save_error"),
                errorMessage);
            return;
        }

        _launcher.RefreshConfigs();
        StatusMessage = LanguageBase.GetLangValue("page_config_save_success");
    }
}
