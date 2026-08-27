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

using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace PDownloader.Installer.Services;

public sealed class InstallService : IInstallService
{
    private const string UninstallRegKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PDownloader";

    private const string PayloadResourceName = "PDownloader.Installer.Resources.payload.zip";
    private const string UpdateTempDirectoryName = "PDownloaderUpdate";

    public string DefaultInstallPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        "PDownloader");

    public string AllUsersDefaultInstallPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "PDownloader");

    public int EstimatedSize => 205824;

    public string GetDefaultInstallPath(InstallScope installScope) =>
        installScope == InstallScope.AllUsers
            ? AllUsersDefaultInstallPath
            : DefaultInstallPath;

    public async Task InstallAsync(
        string installDir,
        InstallScope installScope,
        bool desktopShortcut,
        bool startMenuShortcut,
        bool installBrowserExtension,
        bool runAtStartup,
        IProgress<(double Percent, string Status)> progress,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        await KillAllService(ct);

        await Task.Delay(800, ct);

        string _uninstallDir = GetInstalledDir(installScope)
            ?? GetDefaultInstallPath(installScope);
        if (Directory.Exists(_uninstallDir))
        {
            progress.Report((0.01, Utils.LocalizationHelper.Get("uninstall_progress_title")));
            await UninstallAsync(
                _uninstallDir,
                installScope,
                null,
                ct,
                false);

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
            {
                CreateShortcut(
                    Path.Combine(
                        Environment.GetFolderPath(
                            installScope == InstallScope.AllUsers
                                ? Environment.SpecialFolder.CommonDesktopDirectory
                                : Environment.SpecialFolder.DesktopDirectory),
                        "PDownloader.lnk"),
                    exePath, installDir);
            }

            if (startMenuShortcut)
            {
                string smDir = Path.Combine(
                    Environment.GetFolderPath(
                        installScope == InstallScope.AllUsers
                            ? Environment.SpecialFolder.CommonPrograms
                            : Environment.SpecialFolder.Programs),
                    "PDownloader");
                Directory.CreateDirectory(smDir);
                CreateShortcut(Path.Combine(smDir, "PDownloader.lnk"), exePath, installDir);
            }

            if (runAtStartup)
            {
                SetStartup(true, exePath, installScope);
            }
        }, ct);

        if (installBrowserExtension)
        {
            ct.ThrowIfCancellationRequested();
            progress.Report((0.88, Utils.LocalizationHelper.Get("installing_browser_extension")));
            await Task.Run(
                () => BrowserExtensionInstallerService.InstallForBrowsers(
                    installDir,
                    installScope),
                ct);
        }

        ct.ThrowIfCancellationRequested();
        progress.Report((0.92, Utils.LocalizationHelper.Get("installing_registry")));
        await Task.Run(
            () => RegisterUninstaller(installDir, installScope),
            ct);

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
            {
                continue;
            }

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

    public async Task UninstallAsync(
        string installDir,
        InstallScope installScope,
        IProgress<(double Percent, string Status)>? progress,
        CancellationToken ct,
        bool isCleanupExtension = true,
        bool isCleanupUserData = false)
    {
        ct.ThrowIfCancellationRequested();

        await KillAllService(ct);

        await Task.Delay(800, ct);

        progress?.Report((0.1, Utils.LocalizationHelper.Get("uninstall_removing")));
        string installerExePath = Environment.ProcessPath
            ?? AppContext.BaseDirectory;
        string installerName = Path.GetFileNameWithoutExtension(installerExePath);

        await Task.Run(() =>
        {
            if (!Directory.Exists(installDir))
            {
                return;
            }

            foreach (var file in Directory.GetFiles(installDir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    if (fileName.Equals(installerName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

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
                    {
                        Directory.Delete(dir);
                    }
                }
                catch { }
            }
        }, ct);

        progress?.Report((0.5, Utils.LocalizationHelper.Get("uninstall_removing")));

        await Task.Run(() =>
        {
            var desktopFolders = new List<Environment.SpecialFolder>
            {
                installScope == InstallScope.AllUsers
                    ? Environment.SpecialFolder.CommonDesktopDirectory
                    : Environment.SpecialFolder.DesktopDirectory,
            };

            if (installScope == InstallScope.AllUsers)
            {
                // Older PDownloader installers created the desktop shortcut and
                // startup entry for the installing user even in machine scope.
                desktopFolders.Add(Environment.SpecialFolder.DesktopDirectory);
            }

            foreach (Environment.SpecialFolder desktopFolder in desktopFolders)
            {
                string desktopLnk = Path.Combine(
                    Environment.GetFolderPath(desktopFolder),
                    "PDownloader.lnk");
                if (File.Exists(desktopLnk))
                {
                    File.Delete(desktopLnk);
                }
            }

            string smDir = Path.Combine(
                Environment.GetFolderPath(
                    installScope == InstallScope.AllUsers
                        ? Environment.SpecialFolder.CommonPrograms
                        : Environment.SpecialFolder.Programs),
                "PDownloader");
            if (Directory.Exists(smDir))
            {
                try { Directory.Delete(smDir, true); } catch { }
            }

            SetStartup(false, "", installScope);
            if (installScope == InstallScope.AllUsers)
            {
                SetStartup(false, "", InstallScope.CurrentUser);
            }
        }, ct);

        progress?.Report((0.75, Utils.LocalizationHelper.Get("uninstall_removing")));

        if (isCleanupExtension)
        {
            await Task.Run(
                () => BrowserExtensionInstallerService.UninstallForBrowsers(
                    installScope),
                ct);
        }
        else
        {
            // This is an in-place application update. Preserve the current
            // browser extension registration and remove legacy IDs only.
            await Task.Run(
                () => BrowserExtensionInstallerService.RemoveLegacyExtensionsForBrowsers(
                    installScope),
                ct);
        }

        progress?.Report((0.85, Utils.LocalizationHelper.Get("uninstall_removing")));

        await Task.Run(() =>
        {
            using RegistryKey registryBase = OpenRegistryBaseKey(
                installScope,
                RegistryView.Registry64);
            registryBase.DeleteSubKey(
                UninstallRegKey,
                throwOnMissingSubKey: false);
        }, ct);

        if (isCleanupUserData)
        {
            progress?.Report((0.95, Utils.LocalizationHelper.Get("uninstall_removing")));
            DeleteUserData();
        }

        if (isCleanupExtension)
        {
            ScheduleCleanup(installDir);
        }

        progress?.Report((1.0, Utils.LocalizationHelper.Get("uninstall_done_title")));
    }

    private static void DeleteUserData()
    {
        string path_stored = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SM SOFT", "PDownloader");

        Directory.Delete(path_stored, true);
    }

    private static void ScheduleCleanup(string directory)
    {
        string targetDirectory = NormalizeDirectory(directory);
        string rootDirectory = (Path.GetPathRoot(targetDirectory) ?? string.Empty)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.IsNullOrWhiteSpace(targetDirectory)
            || targetDirectory.Equals(rootDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            // Keep the cleanup script outside the directory that it will remove.
            // It waits for this installer PID to disappear before deleting files,
            // so the downloaded installer is no longer locked by Windows.
            string cleanupDirectory = Path.Combine(Path.GetTempPath(), "PDownloaderCleanup");
            Directory.CreateDirectory(cleanupDirectory);

            string scriptPath = Path.Combine(
                cleanupDirectory,
                $"cleanup-{Environment.ProcessId}-{Guid.NewGuid():N}.cmd");

            string script = """
                @echo off
                setlocal DisableDelayedExpansion
                set "TARGET=%PDOWNLOADER_CLEANUP_TARGET%"

                rem The running installer remains locked during the first attempts.
                rem Retry until Windows releases it after the process has exited.
                for /L %%i in (1,1,120) do (
                    rd /s /q "%TARGET%" >nul 2>&1
                    if not exist "%TARGET%" goto cleanup_done
                    timeout /t 1 /nobreak >nul
                )

                :cleanup_done
                endlocal
                del /f /q "%~f0" >nul 2>&1
                exit /b 0
                """;

            File.WriteAllText(
                scriptPath,
                script.ReplaceLineEndings(Environment.NewLine));

            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = cleanupDirectory,
            };

            startInfo.Environment["PDOWNLOADER_CLEANUP_TARGET"] = targetDirectory;

            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/q");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(scriptPath);

            Process.Start(startInfo);
        }
        catch
        {
            // Cleanup is best-effort and must never block installer shutdown.
        }
    }

    public void ScheduleTemporaryFilesCleanup(string? requestedUpdateTempDirectory = null)
    {
        try
        {
            string tempRoot = NormalizeDirectory(Path.GetTempPath());
            string expectedUpdateDirectory = NormalizeDirectory(
                Path.Combine(tempRoot, UpdateTempDirectoryName));

            string? updateDirectory = ResolveUpdateTempDirectory(
                requestedUpdateTempDirectory,
                expectedUpdateDirectory);

            if (updateDirectory is not null)
            {
                ScheduleCleanup(updateDirectory);
            }

            string baseDirectory = NormalizeDirectory(AppContext.BaseDirectory);
            string netTempRoot = NormalizeDirectory(Path.Combine(tempRoot, ".net"));

            if (IsSameDirectoryOrChild(baseDirectory, netTempRoot))
            {
                ScheduleCleanup(baseDirectory);
            }
        }
        catch
        {
            // Cleanup is best-effort and must never block installer shutdown.
        }
    }

    private static string? ResolveUpdateTempDirectory(
        string? requestedDirectory,
        string expectedUpdateDirectory)
    {
        // Accept only the exact application-owned update folder. Never trust a
        // command-line path as an arbitrary recursive-delete target.
        if (!string.IsNullOrWhiteSpace(requestedDirectory))
        {
            string normalizedRequested = NormalizeDirectory(requestedDirectory);
            if (normalizedRequested.Equals(
                    expectedUpdateDirectory,
                    StringComparison.OrdinalIgnoreCase))
            {
                return expectedUpdateDirectory;
            }
        }

        string processDirectory = Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty)
            ?? string.Empty;
        if (IsSameDirectoryOrChild(processDirectory, expectedUpdateDirectory))
        {
            return expectedUpdateDirectory;
        }

        string commandLineExecutable = Environment.GetCommandLineArgs().FirstOrDefault()
            ?? string.Empty;
        string commandLineDirectory = Path.GetDirectoryName(commandLineExecutable)
            ?? string.Empty;
        if (IsSameDirectoryOrChild(commandLineDirectory, expectedUpdateDirectory))
        {
            return expectedUpdateDirectory;
        }

        // Compatibility fallback for an update launched by an older app build
        // that does not yet pass --update-temp-dir. Only target the fixed,
        // application-owned folder and only when a completed installer exists.
        try
        {
            bool containsDownloadedInstaller = Directory.Exists(expectedUpdateDirectory)
                && Directory.EnumerateFiles(
                        expectedUpdateDirectory,
                        "PDownloader.Installer*.exe",
                        SearchOption.TopDirectoryOnly)
                    .Any();

            if (containsDownloadedInstaller)
            {
                return expectedUpdateDirectory;
            }
        }
        catch
        {
            // Fall through when the directory cannot be inspected.
        }

        return null;
    }

    private static string NormalizeDirectory(string directory)
    {
        return Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsSameDirectoryOrChild(string candidate, string root)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        string candidatePath = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string rootPath = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return candidatePath.Equals(rootPath, StringComparison.OrdinalIgnoreCase)
            || candidatePath.StartsWith(
                rootPath + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private void RegisterUninstaller(
        string installDir,
        InstallScope installScope)
    {
        string exePath = Path.Combine(installDir, "PDownloader.exe");
        string uninstallerExe = Path.Combine(installDir, "PDownloader.Installer.exe");

        Version? AssemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        string _appVersion = AssemblyName != null ? AssemblyName.ToString() : "0.0.0.0";

        using RegistryKey registryBase = OpenRegistryBaseKey(
            installScope,
            RegistryView.Registry64);
        using RegistryKey key = registryBase.CreateSubKey(UninstallRegKey);
        key.SetValue("DisplayName", "PDownloader");
        key.SetValue("DisplayVersion", _appVersion);
        key.SetValue("Publisher", "PDownloader");
        key.SetValue("InstallLocation", installDir);
        key.SetValue("DisplayIcon", exePath);
        key.SetValue("UninstallString",
            $"\"{uninstallerExe}\" --uninstall {GetScopeArgument(installScope)}");
        key.SetValue("QuietUninstallString",
            $"\"{uninstallerExe}\" --uninstall --quiet {GetScopeArgument(installScope)}");
        key.SetValue(
            "InstallScope",
            installScope.ToString(),
            RegistryValueKind.String);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", EstimatedSize, RegistryValueKind.DWord);
    }

    public string? GetInstalledDir(InstallScope installScope)
    {
        using RegistryKey registryBase = OpenRegistryBaseKey(
            installScope,
            RegistryView.Registry64);
        using RegistryKey? key = registryBase.OpenSubKey(UninstallRegKey);
        return key?.GetValue("InstallLocation") as string;
    }

    public InstallScope? GetInstalledScope()
    {
        if (!string.IsNullOrWhiteSpace(GetInstalledDir(InstallScope.CurrentUser)))
        {
            return InstallScope.CurrentUser;
        }

        return !string.IsNullOrWhiteSpace(GetInstalledDir(InstallScope.AllUsers))
            ? InstallScope.AllUsers
            : null;
    }

    private static void CreateShortcut(string lnkPath, string targetPath, string workDir)
    {
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null)
        {
            return;
        }

        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(lnkPath);
        shortcut.TargetPath = targetPath;
        shortcut.WorkingDirectory = workDir;
        shortcut.Description = "PDownloader";
        shortcut.Save();
    }

    private static void SetStartup(
        bool enable,
        string exePath,
        InstallScope installScope)
    {
        const string startupKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        using RegistryKey registryBase = OpenRegistryBaseKey(
            installScope,
            RegistryView.Registry64);
        using RegistryKey? key = enable
            ? registryBase.CreateSubKey(startupKey, writable: true)
            : registryBase.OpenSubKey(startupKey, writable: true);
        if (key == null)
        {
            return;
        }

        if (enable)
        {
            key.SetValue("PDownloader", $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue("PDownloader", throwOnMissingValue: false);
        }
    }

    private static RegistryKey OpenRegistryBaseKey(
        InstallScope installScope,
        RegistryView registryView)
    {
        RegistryHive registryHive = installScope == InstallScope.AllUsers
            ? RegistryHive.LocalMachine
            : RegistryHive.CurrentUser;

        return RegistryKey.OpenBaseKey(registryHive, registryView);
    }

    private static string GetScopeArgument(InstallScope installScope) =>
        installScope == InstallScope.AllUsers
            ? "--all-users"
            : "--just-me";

    private static async Task KillAllService(CancellationToken ct)
    {
        await Task.Run(() =>
        {
            foreach (Process p in Process.GetProcessesByName("PDownloader"))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
            }

            foreach (Process p in Process.GetProcessesByName("PDownloader Tray"))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
            }

            foreach (Process p in Process.GetProcessesByName("PDownloader Core"))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
            }

            foreach (Process p in Process.GetProcessesByName("PDownloader Runner"))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
            }
        }, ct);
    }
}
