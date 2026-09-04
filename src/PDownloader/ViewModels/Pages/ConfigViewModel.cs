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
    private readonly DownloadsChannelService _downloadsChannel;

    [ObservableProperty]
    private DownloadConfigs? _downloadConfigs;

    [ObservableProperty]
    private bool _isDaemonRunning;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private DownloadCategoryViewModel? _selectedDownloadCategory;

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

    public ConfigViewModel(
        DownloadLauncherService launcher,
        DownloadConfigService configService,
        DownloadsChannelService downloadsChannel)
    {
        _configService = configService;
        _downloadConfigs = configService.DownloadConfigs;
        _launcher = launcher;
        _downloadsChannel = downloadsChannel;
        _downloadsChannel.OnDownloadSettingsChanged += ApplyDownloadSettings;
        SelectedDownloadCategory = DownloadConfigs?.DownloadCategories.FirstOrDefault();

        IsDaemonRunning = _launcher.IsDaemonRunning;
        StatusMessage = IsDaemonRunning
            ? LanguageBase.GetLangValue("page_config_svc_active_title")
            : LanguageBase.GetLangValue("page_config_svc_inactive_title");
    }

    partial void OnSelectedDownloadCategoryChanged(DownloadCategoryViewModel? value)
    {
        RemoveDownloadCategoryCommand.NotifyCanExecuteChanged();
        MoveDownloadCategoryUpCommand.NotifyCanExecuteChanged();
        MoveDownloadCategoryDownCommand.NotifyCanExecuteChanged();
        BrowseCategoryFolderCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void AddDownloadCategory()
    {
        if (DownloadConfigs is null)
        {
            return;
        }

        string name = LanguageBase.GetLangValue("page_config_groups_new_name");
        string root = DownloadConfigs.DefaultDownloadFolder;
        string folderName = "Custom";
        string path = Path.Combine(root, folderName);
        int suffix = 2;
        while (DownloadConfigs.DownloadCategories.Any(category =>
                   string.Equals(category.FolderPath, path, StringComparison.OrdinalIgnoreCase)))
        {
            path = Path.Combine(root, folderName + " " + suffix.ToString(CultureInfo.InvariantCulture));
            suffix++;
        }

        var category = new DownloadCategoryViewModel(
            "custom-" + Guid.NewGuid().ToString("N"),
            name,
            path,
            string.Empty,
            true);
        DownloadConfigs.DownloadCategories.Add(category);
        SelectedDownloadCategory = category;
        RefreshCategoryCommands();
    }

    private bool CanRemoveDownloadCategory() =>
        SelectedDownloadCategory is not null
        && (DownloadConfigs?.DownloadCategories.Count ?? 0) > 1;

    [RelayCommand(CanExecute = nameof(CanRemoveDownloadCategory))]
    private void RemoveDownloadCategory()
    {
        if (DownloadConfigs is null || SelectedDownloadCategory is null)
        {
            return;
        }

        int index = DownloadConfigs.DownloadCategories.IndexOf(SelectedDownloadCategory);
        DownloadConfigs.DownloadCategories.Remove(SelectedDownloadCategory);
        SelectedDownloadCategory = DownloadConfigs.DownloadCategories.Count == 0
            ? null
            : DownloadConfigs.DownloadCategories[Math.Min(index,
                DownloadConfigs.DownloadCategories.Count - 1)];
        RefreshCategoryCommands();
    }

    private bool CanMoveDownloadCategoryUp() =>
        DownloadConfigs is not null
        && SelectedDownloadCategory is not null
        && DownloadConfigs.DownloadCategories.IndexOf(SelectedDownloadCategory) > 0;

    [RelayCommand(CanExecute = nameof(CanMoveDownloadCategoryUp))]
    private void MoveDownloadCategoryUp()
    {
        if (DownloadConfigs is null || SelectedDownloadCategory is null)
        {
            return;
        }

        int index = DownloadConfigs.DownloadCategories.IndexOf(SelectedDownloadCategory);
        DownloadConfigs.DownloadCategories.Move(index, index - 1);
        RefreshCategoryCommands();
    }

    private bool CanMoveDownloadCategoryDown() =>
        DownloadConfigs is not null
        && SelectedDownloadCategory is not null
        && DownloadConfigs.DownloadCategories.IndexOf(SelectedDownloadCategory)
            < DownloadConfigs.DownloadCategories.Count - 1;

    [RelayCommand(CanExecute = nameof(CanMoveDownloadCategoryDown))]
    private void MoveDownloadCategoryDown()
    {
        if (DownloadConfigs is null || SelectedDownloadCategory is null)
        {
            return;
        }

        int index = DownloadConfigs.DownloadCategories.IndexOf(SelectedDownloadCategory);
        DownloadConfigs.DownloadCategories.Move(index, index + 1);
        RefreshCategoryCommands();
    }

    private bool CanBrowseCategoryFolder() => SelectedDownloadCategory is not null;

    [RelayCommand(CanExecute = nameof(CanBrowseCategoryFolder))]
    private void BrowseCategoryFolder()
    {
        if (SelectedDownloadCategory is null)
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = LanguageBase.GetLangValue("page_config_groups_path_title"),
            InitialDirectory = Directory.Exists(SelectedDownloadCategory.FolderPath)
                ? SelectedDownloadCategory.FolderPath
                : DownloadConfigs?.DefaultDownloadFolder,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            SelectedDownloadCategory.FolderPath = dialog.FolderName;
        }
    }

    private void ApplyDownloadSettings(DownloadSettingsDto settings)
    {
        void Apply()
        {
            if (DownloadConfigs is null)
            {
                return;
            }

            string? selectedId = SelectedDownloadCategory?.Id;
            DownloadConfigs.ApplyContract(settings);
            SelectedDownloadCategory = DownloadConfigs.DownloadCategories.FirstOrDefault(category =>
                string.Equals(category.Id, selectedId, StringComparison.OrdinalIgnoreCase))
                ?? DownloadConfigs.DownloadCategories.FirstOrDefault();
            RefreshCategoryCommands();
        }

        if (Application.Current?.Dispatcher.CheckAccess() == false)
        {
            Application.Current.Dispatcher.Invoke(Apply);
        }
        else
        {
            Apply();
        }
    }

    private void RefreshCategoryCommands()
    {
        RemoveDownloadCategoryCommand.NotifyCanExecuteChanged();
        MoveDownloadCategoryUpCommand.NotifyCanExecuteChanged();
        MoveDownloadCategoryDownCommand.NotifyCanExecuteChanged();
        BrowseCategoryFolderCommand.NotifyCanExecuteChanged();
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
