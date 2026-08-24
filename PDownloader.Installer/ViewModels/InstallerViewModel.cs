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

using System.Reflection;

namespace PDownloader.Installer.ViewModels;

public enum InstallerStep
{
    Language,
    Welcome,
    License,
    InstallPath,
    Options,
    Installing,
    Finish,
    Error,
    UninstallConfirm,
    Uninstalling,
    UninstallDone,
}

public partial class InstallerViewModel : ObservableObject
{
    private readonly IInstallService _installService;
    private readonly ILicenseService _licenseService;
    private readonly IFolderPickerService _folderPickerService;
    private readonly IInstallerApplicationService _applicationService;
    private readonly string _uninstallDirectory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepIndex))]
    private InstallerStep _step = InstallerStep.Language;

    [ObservableProperty]
    private string _installPath;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SelectLanguageCommand))]
    private LanguageItem? _selectedLanguage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextFromLicenseCommand))]
    private bool _licenseAccepted;

    [ObservableProperty]
    private string _licenseText;

    [ObservableProperty]
    private bool _desktopShortcut = true;

    [ObservableProperty]
    private bool _startMenuShortcut = true;

    [ObservableProperty]
    private bool _installBrowserExtension = true;

    [ObservableProperty]
    private bool _runAtStartup;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _launchAfterInstall = true;

    [ObservableProperty]
    private string _errorDetail = string.Empty;

    public InstallerViewModel(
        InstallerLaunchOptions launchOptions,
        IInstallService installService,
        ILicenseService licenseService,
        IFolderPickerService folderPickerService,
        IInstallerApplicationService applicationService)
    {
        _installService = installService;
        _licenseService = licenseService;
        _folderPickerService = folderPickerService;
        _applicationService = applicationService;

        IsUninstallMode = launchOptions.IsUninstallMode;
        _installPath = installService.DefaultInstallPath;
        _uninstallDirectory = installService.GetInstalledDir()
            ?? installService.DefaultInstallPath;
        _installBrowserExtension = launchOptions.InstallBrowserExtension;
        _runAtStartup = UserDataStore.GetValue<bool>("IsStartAtBoot");
        _selectedLanguage = Languages.FirstOrDefault(language => language.Code == "en")
            ?? Languages.First();
        _licenseText = licenseService.Load(_selectedLanguage.Code);
    }

    public ObservableCollection<LanguageItem> Languages { get; } =
        LanguageBase.GetLanguageItems();

    public bool IsUninstallMode { get; }

    public int EstimatedSize => _installService.EstimatedSize / 1024;

    public string AppVersion { get; } = GetApplicationVersion();

    public int StepIndex => Step switch
    {
        InstallerStep.Language => 0,
        InstallerStep.Welcome => 1,
        InstallerStep.License => 2,
        InstallerStep.InstallPath => 3,
        InstallerStep.Options => 4,
        InstallerStep.Installing => 5,
        InstallerStep.Finish => 6,
        _ => -1,
    };

    private bool CanSelectLanguage() => SelectedLanguage is not null;

    [RelayCommand(CanExecute = nameof(CanSelectLanguage))]
    private void SelectLanguage()
    {
        if (SelectedLanguage is null)
        {
            return;
        }

        LanguageBase.SetLanguage(SelectedLanguage.Code);
        LicenseText = _licenseService.Load(SelectedLanguage.Code);
        Step = IsUninstallMode
            ? InstallerStep.UninstallConfirm
            : InstallerStep.Welcome;
    }

    [RelayCommand]
    private void NextFromWelcome() =>
        Step = InstallerStep.License;

    private bool CanGoNextFromLicense() => LicenseAccepted;

    [RelayCommand(CanExecute = nameof(CanGoNextFromLicense))]
    private void NextFromLicense() =>
        Step = InstallerStep.InstallPath;

    [RelayCommand]
    private void BackFromLicense() =>
        Step = InstallerStep.Welcome;

    [RelayCommand]
    private void BrowseFolder()
    {
        string? selectedPath = _folderPickerService.PickFolder(
            LocalizationHelper.Get("path_label"),
            InstallPath);

        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            InstallPath = selectedPath;
        }
    }

    [RelayCommand]
    private void NextFromPath() =>
        Step = InstallerStep.Options;

    [RelayCommand]
    private void BackFromPath() =>
        Step = InstallerStep.License;

    [RelayCommand]
    private void BackFromOptions() =>
        Step = InstallerStep.InstallPath;

    [RelayCommand]
    private async Task NextFromOptionsAsync()
    {
        Step = InstallerStep.Installing;
        ResetOperationState();

        var progress = new Progress<(double Percent, string Status)>(result =>
        {
            Progress = result.Percent * 100;
            StatusText = result.Status;
        });

        try
        {
            await _installService.InstallAsync(
                InstallPath,
                DesktopShortcut,
                StartMenuShortcut,
                InstallBrowserExtension,
                RunAtStartup,
                progress,
                CancellationToken.None);
            Step = InstallerStep.Finish;
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    [RelayCommand]
    private async Task UninstallConfirmAsync()
    {
        Step = InstallerStep.Uninstalling;
        ResetOperationState();

        var progress = new Progress<(double Percent, string Status)>(result =>
        {
            Progress = result.Percent * 100;
            StatusText = result.Status;
        });

        try
        {
            await _installService.UninstallAsync(
                _uninstallDirectory,
                progress,
                CancellationToken.None);
            Step = InstallerStep.UninstallDone;
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    [RelayCommand]
    private void Finish()
    {
        if (LaunchAfterInstall)
        {
            _applicationService.TryLaunch(
                Path.Combine(InstallPath, "PDownloader.exe"),
                InstallPath);
        }

        _applicationService.Shutdown();
    }

    [RelayCommand]
    private void Close() =>
        _applicationService.Shutdown();

    private void ResetOperationState()
    {
        Progress = 0;
        StatusText = string.Empty;
        ErrorDetail = string.Empty;
    }

    private void ShowError(Exception exception)
    {
        ErrorDetail = exception.Message;
        Step = InstallerStep.Error;
    }

    private static string GetApplicationVersion()
    {
        Version? version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null
            ? "Unknown"
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
