using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace PDownloader.Installer.Services
{
    public static class InstallService
    {
        private const string UninstallRegKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PDownloader";

        private const string PayloadResourceName = "PDownloader.Installer.Resources.payload.zip";

        public static string DefaultInstallPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PDownloader");

        public static async Task InstallAsync(
            string installDir,
            bool desktopShortcut,
            bool startMenuShortcut,
            bool runAtStartup,
            IProgress<(double Percent, string Status)> progress,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            await KillAllService(ct);

            await Task.Delay(800, ct);

            string _uninstallDir = GetInstalledDir() ?? DefaultInstallPath;
            if (Directory.Exists(_uninstallDir))
            {
                progress.Report((0.01, Utils.LocalizationHelper.Get("uninstall_progress_title")));
                await UninstallAsync(_uninstallDir, null, ct, false);

                await Task.Delay(800, ct);
            }

            progress.Report((0.05, Utils.LocalizationHelper.Get("installing_copying")));
            Directory.CreateDirectory(installDir);
            await Task.Run(() => ExtractPayload(installDir, progress, ct), ct);

            ct.ThrowIfCancellationRequested();
            progress.Report((0.80, Utils.LocalizationHelper.Get("installing_shortcuts")));
            await Task.Run(() =>
            {
                string exePath = Path.Combine(installDir, "PDownloader.exe");
                if (desktopShortcut)
                    CreateShortcut(
                        Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                            "PDownloader.lnk"),
                        exePath, installDir);

                if (startMenuShortcut)
                {
                    string smDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                        "PDownloader");
                    Directory.CreateDirectory(smDir);
                    CreateShortcut(Path.Combine(smDir, "PDownloader.lnk"), exePath, installDir);
                }

                if (runAtStartup)
                    SetStartup(true, exePath);
            }, ct);

            ct.ThrowIfCancellationRequested();
            progress.Report((0.92, Utils.LocalizationHelper.Get("installing_registry")));
            await Task.Run(() => RegisterUninstaller(installDir), ct);

            progress.Report((1.0, Utils.LocalizationHelper.Get("installing_done")));
        }

        private static void ExtractPayload(
            string installDir,
            IProgress<(double, string)> progress,
            CancellationToken ct)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            using Stream? resourceStream = asm.GetManifestResourceStream(PayloadResourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded resource '{PayloadResourceName}' not found. " +
                    "Ensure build.bat ran successfully and payload.zip was created before publishing.");

            using ZipArchive zip = new ZipArchive(resourceStream, ZipArchiveMode.Read);

            int total = zip.Entries.Count;
            int done = 0;

            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();

                string destPath = Path.GetFullPath(Path.Combine(installDir, entry.FullName));

                if (!destPath.StartsWith(Path.GetFullPath(installDir) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destPath);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    entry.ExtractToFile(destPath, overwrite: true);
                }

                done++;
                progress.Report((
                    0.05 + 0.70 * done / Math.Max(total, 1),
                    Utils.LocalizationHelper.Get("installing_copying")));
            }
        }

        public static async Task UninstallAsync(
            string installDir,
            IProgress<(double Percent, string Status)>? progress,
            CancellationToken ct,
            bool isCleanup = true)
        {
            ct.ThrowIfCancellationRequested();

            await KillAllService(ct);

            await Task.Delay(800, ct);

            progress?.Report((0.1, Utils.LocalizationHelper.Get("uninstall_removing")));
            string installerExePath = Environment.ProcessPath
                ?? Assembly.GetExecutingAssembly().Location;
            string installerName = Path.GetFileNameWithoutExtension(installerExePath);

            await Task.Run(() =>
            {
                if (!Directory.Exists(installDir)) return;

                foreach (var file in Directory.GetFiles(installDir, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        string fileName = Path.GetFileNameWithoutExtension(file);
                        if (fileName.Equals(installerName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        File.Delete(file);
                    }
                    catch { }
                }

                foreach (var dir in Directory.GetDirectories(installDir, "*", SearchOption.AllDirectories)
                                             .OrderByDescending(d => d.Length))
                {
                    try
                    {
                        if (Directory.GetFiles(dir).Length == 0 &&
                            Directory.GetDirectories(dir).Length == 0)
                            Directory.Delete(dir);
                    }
                    catch { }
                }
            }, ct);

            progress?.Report((0.5, Utils.LocalizationHelper.Get("uninstall_removing")));

            await Task.Run(() =>
            {
                string desktopLnk = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "PDownloader.lnk");
                if (File.Exists(desktopLnk)) File.Delete(desktopLnk);

                string smDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                    "PDownloader");
                if (Directory.Exists(smDir))
                    try { Directory.Delete(smDir, true); } catch { }

                SetStartup(false, "");
            }, ct);

            progress?.Report((0.85, Utils.LocalizationHelper.Get("uninstall_removing")));

            await Task.Run(() =>
            {
                Registry.LocalMachine.DeleteSubKey(UninstallRegKey, throwOnMissingSubKey: false);
            }, ct);

            if (isCleanup)
            {
                ScheduleCleanup(installDir);
            }

            progress?.Report((1.0, Utils.LocalizationHelper.Get("uninstall_done_title")));
        }

        private static void ScheduleCleanup(string installDir)
        {
            string escapedDir = installDir.TrimEnd('\\');
            string args = $"/c timeout /t 3 /nobreak >nul & " +
                          $"rd /s /q \"{escapedDir}\"";

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = args,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                });
            }
            catch { }
        }

        private static void RegisterUninstaller(string installDir)
        {
            string exePath = Path.Combine(installDir, "PDownloader.exe");
            string uninstallerExe = Path.Combine(installDir, "PDownloader.Installer.exe");

            using var key = Registry.LocalMachine.CreateSubKey(UninstallRegKey);
            key.SetValue("DisplayName", "PDownloader");
            key.SetValue("DisplayVersion", "1.0.0");
            key.SetValue("Publisher", "PDownloader");
            key.SetValue("InstallLocation", installDir);
            key.SetValue("DisplayIcon", exePath);
            key.SetValue("UninstallString",
                $"\"{uninstallerExe}\" --uninstall");
            key.SetValue("QuietUninstallString",
                $"\"{uninstallerExe}\" --uninstall --quiet");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", 51200, RegistryValueKind.DWord);
        }

        public static string? GetInstalledDir()
        {
            using var key = Registry.LocalMachine.OpenSubKey(UninstallRegKey);
            return key?.GetValue("InstallLocation") as string;
        }

        private static void CreateShortcut(string lnkPath, string targetPath, string workDir)
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(lnkPath);
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = workDir;
            shortcut.Description = "PDownloader";
            shortcut.Save();
        }

        private static void SetStartup(bool enable, string exePath)
        {
            const string startupKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
            using var key = Registry.CurrentUser.OpenSubKey(startupKey, writable: true);
            if (key == null) return;
            if (enable)
                key.SetValue("PDownloader", $"\"{exePath}\"");
            else
                key.DeleteValue("PDownloader", throwOnMissingValue: false);
        }

        private static async Task KillAllService(CancellationToken ct)
        {
            await Task.Run(() =>
            {
                foreach (var p in Process.GetProcessesByName("PDownloader"))
                    try { p.Kill(entireProcessTree: true); } catch { }
                foreach (var p in Process.GetProcessesByName("PDownloader Tray"))
                    try { p.Kill(entireProcessTree: true); } catch { }
                foreach (var p in Process.GetProcessesByName("PDownloader Core"))
                    try { p.Kill(entireProcessTree: true); } catch { }
                foreach (var p in Process.GetProcessesByName("PDownloader Overlay"))
                    try { p.Kill(entireProcessTree: true); } catch { }
            }, ct);
        }
    }
}
