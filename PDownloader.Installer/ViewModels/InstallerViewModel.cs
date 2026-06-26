using PDownloader.Installer.Services;
using PDownloader.Installer.Utils;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace PDownloader.Installer.ViewModels
{
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

        // Uninstall flow
        UninstallConfirm,
        Uninstalling,
        UninstallDone,
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;
        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        { _execute = execute; _canExecute = canExecute; }
        public bool CanExecute(object? p) => _canExecute?.Invoke() ?? true;
        public void Execute(object? p) => _execute();
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }

    public class InstallerViewModel : INotifyPropertyChanged
    {
        // ------------------------------------------------------------------ //
        //  State
        // ------------------------------------------------------------------ //

        private InstallerStep _step = InstallerStep.Language;
        public InstallerStep Step
        {
            get => _step;
            set { _step = value; OnPropertyChanged(); OnPropertyChanged(nameof(StepIndex)); }
        }

        // Sidebar step indicator (0-based, only for install flow)
        public int StepIndex => _step switch
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

        private static System.Version? AssemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        private string? _appVersion = AssemblyName != null ? $"{AssemblyName.Major}.{AssemblyName.Minor}.{AssemblyName.Build}" : null;

        public string AppVersion { 
            get => _appVersion ?? "Unknown";
            set { _appVersion = value; OnPropertyChanged(); }
        }

        // -- Language --
        public ObservableCollection<LanguageItem> Languages { get; } =
            LanguageBase.GetLanguageItems();

        private LanguageItem? _selectedLanguage;
        public LanguageItem? SelectedLanguage
        {
            get => _selectedLanguage;
            set { _selectedLanguage = value; OnPropertyChanged(); }
        }

        // -- License --
        private bool _licenseAccepted;
        public bool LicenseAccepted
        {
            get => _licenseAccepted;
            set { _licenseAccepted = value; OnPropertyChanged(); }
        }

        private string _licenseText = "";
        public string LicenseText
        {
            get => _licenseText;
            set { _licenseText = value; OnPropertyChanged(); }
        }

        // -- Install Path --
        private string _installPath = InstallService.DefaultInstallPath;
        public string InstallPath
        {
            get => _installPath;
            set { _installPath = value; OnPropertyChanged(); }
        }

        // -- Options --
        private bool _desktopShortcut = true;
        public bool DesktopShortcut
        {
            get => _desktopShortcut;
            set { _desktopShortcut = value; OnPropertyChanged(); }
        }

        private bool _startMenuShortcut = true;
        public bool StartMenuShortcut
        {
            get => _startMenuShortcut;
            set { _startMenuShortcut = value; OnPropertyChanged(); }
        }

        private bool _runAtStartup = UserDataStore.GetValue<bool>("IsStartAtBoot");
        public bool RunAtStartup
        {
            get => _runAtStartup;
            set { _runAtStartup = value; OnPropertyChanged(); }
        }

        // -- Progress --
        private double _progress;
        public double Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(); }
        }

        private string _statusText = "";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        // -- Finish --
        private bool _launchAfterInstall = true;
        public bool LaunchAfterInstall
        {
            get => _launchAfterInstall;
            set { _launchAfterInstall = value; OnPropertyChanged(); }
        }

        // -- Error --
        private string _errorDetail = "";
        public string ErrorDetail
        {
            get => _errorDetail;
            set { _errorDetail = value; OnPropertyChanged(); }
        }

        // -- Uninstall --
        public bool IsUninstallMode { get; }

        private string _uninstallDir = "";

        // ------------------------------------------------------------------ //
        //  Commands
        // ------------------------------------------------------------------ //

        public ICommand SelectLanguageCmd { get; }
        public ICommand NextFromWelcomeCmd { get; }
        public ICommand NextFromLicenseCmd { get; }
        public ICommand BackFromLicenseCmd { get; }
        public ICommand BrowseFolderCmd { get; }
        public ICommand NextFromPathCmd { get; }
        public ICommand BackFromPathCmd { get; }
        public ICommand NextFromOptionsCmd { get; }
        public ICommand BackFromOptionsCmd { get; }
        public ICommand FinishCmd { get; }
        public ICommand UninstallConfirmCmd { get; }
        public ICommand UninstallCancelCmd { get; }
        public ICommand UninstallDoneCmd { get; }

        // ------------------------------------------------------------------ //
        //  Constructor
        // ------------------------------------------------------------------ //

        public InstallerViewModel(bool uninstallMode = false)
        {
            IsUninstallMode = uninstallMode;
            if (uninstallMode)
            {
                _uninstallDir = InstallService.GetInstalledDir() ?? InstallService.DefaultInstallPath;
                Step = InstallerStep.Language;
            }

            // Init language to system default (prefer vi if available)
            _selectedLanguage = Languages.FirstOrDefault(l => l.Code == "en")
                ?? Languages.First();

            // Load license text ngay khi khởi tạo
            _licenseText = LoadLicenseText(SelectedLanguage?.Code ?? "en");

            SelectLanguageCmd = new RelayCommand(OnSelectLanguage,
                () => SelectedLanguage != null);

            NextFromWelcomeCmd = new RelayCommand(() => Step = InstallerStep.License);
            NextFromLicenseCmd = new RelayCommand(
                () => Step = InstallerStep.InstallPath,
                () => LicenseAccepted);
            BackFromLicenseCmd = new RelayCommand(() => Step = InstallerStep.Welcome);

            BrowseFolderCmd = new RelayCommand(OnBrowseFolder);
            NextFromPathCmd = new RelayCommand(() => Step = InstallerStep.Options);
            BackFromPathCmd = new RelayCommand(() => Step = InstallerStep.License);

            NextFromOptionsCmd = new RelayCommand(async () => await StartInstallAsync());
            BackFromOptionsCmd = new RelayCommand(() => Step = InstallerStep.InstallPath);

            FinishCmd = new RelayCommand(OnFinish);

            UninstallConfirmCmd = new RelayCommand(async () => await StartUninstallAsync());
            UninstallCancelCmd = new RelayCommand(() => System.Windows.Application.Current.Shutdown());
            UninstallDoneCmd = new RelayCommand(() => System.Windows.Application.Current.Shutdown());
        }

        // ------------------------------------------------------------------ //
        //  Handlers
        // ------------------------------------------------------------------ //

        private static string LoadLicenseText(string languageCode)
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();

            if (!string.IsNullOrEmpty(languageCode) && languageCode != "en")
            {
                using Stream? localizedStream = asm.GetManifestResourceStream(
                    $"PDownloader.Installer.LICENSE.{languageCode}");
                if (localizedStream is not null)
                    using (var reader = new StreamReader(localizedStream))
                        return reader.ReadToEnd();
            }

            using Stream? enStream = asm.GetManifestResourceStream(
                "PDownloader.Installer.LICENSE");
            if (enStream is not null)
                using (var reader = new StreamReader(enStream))
                    return reader.ReadToEnd();

            string installerDir = Path.GetDirectoryName(
                Environment.ProcessPath
                ?? System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";

            string[] candidates =
            {
                Path.Combine(installerDir, $"LICENSE.{languageCode}"),
                Path.Combine(installerDir, "LICENSE"),
                Path.Combine(installerDir, "..", $"LICENSE.{languageCode}"),
                Path.Combine(installerDir, "..", "LICENSE"),
            };

            foreach (var path in candidates)
                if (File.Exists(path))
                    return File.ReadAllText(path);

            return """
MIT License

Copyright (c) 2024 PDownloader

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
""";
        }

        private void OnSelectLanguage()
        {
            if (SelectedLanguage == null) return;
            LanguageBase.SetLanguage(SelectedLanguage.Code);

            Step = IsUninstallMode ? InstallerStep.UninstallConfirm : InstallerStep.Welcome;

            _licenseText = LoadLicenseText(SelectedLanguage?.Code ?? "en");
        }

        private void OnBrowseFolder()
        {
            // WPF không có FolderBrowserDialog built-in → dùng OpenFileDialog trick hoặc
            // WinForms FolderBrowserDialog (safe trong WPF).
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = LocalizationHelper.Get("path_label"),
                SelectedPath = InstallPath,
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true,
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                InstallPath = dlg.SelectedPath;
        }

        private async Task StartInstallAsync()
        {
            Step = InstallerStep.Installing;
            var cts = new CancellationTokenSource();
            var prog = new Progress<(double Percent, string Status)>(r =>
            {
                Progress = r.Percent * 100;
                StatusText = r.Status;
            });

            try
            {
                await InstallService.InstallAsync(
                    InstallPath,
                    DesktopShortcut,
                    StartMenuShortcut,
                    RunAtStartup,
                    prog,
                    cts.Token);
                Step = InstallerStep.Finish;
            }
            catch (Exception ex)
            {
                ErrorDetail = ex.Message;
                Step = InstallerStep.Error;
            }
        }

        private async Task StartUninstallAsync()
        {
            Step = InstallerStep.Uninstalling;
            var prog = new Progress<(double Percent, string Status)>(r =>
            {
                Progress = r.Percent * 100;
                StatusText = r.Status;
            });
            try
            {
                await InstallService.UninstallAsync(
                    _uninstallDir, prog, CancellationToken.None);
                Step = InstallerStep.UninstallDone;
            }
            catch (Exception ex)
            {
                ErrorDetail = ex.Message;
                Step = InstallerStep.Error;
            }
        }

        private void OnFinish()
        {
            if (LaunchAfterInstall)
            {
                string exe = System.IO.Path.Combine(InstallPath, "PDownloader.exe");
                if (System.IO.File.Exists(exe))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
                    {
                        UseShellExecute = true,
                        WorkingDirectory = InstallPath,
                    });
            }
            System.Windows.Application.Current.Shutdown();
        }

        // ------------------------------------------------------------------ //
        //  INotifyPropertyChanged
        // ------------------------------------------------------------------ //

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
