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

using System.Net.Http;
using System.Net.Http.Json;

namespace PDownloader.Services.UpdateServices;

public class UpdateService
{
    private const string GitHubOwner = "maihcx";
    private const string GitHubRepo = "PDownloader";
    private const string UpdateTempDirectoryName = "PDownloaderUpdate";
    private const string PendingUpdateMarkerName = "pending-update.json";

    private static readonly HttpClient _http = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "PDownloader-Updater" } },
        Timeout = TimeSpan.FromSeconds(30),
    };

    public GitHubRelease? LatestRelease { get; private set; }
    public string? InstallerDownloadUrl { get; private set; }
    public long InstallerSize { get; private set; }
    public string? DownloadedInstallerPath { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task<bool> CheckForUpdateAsync(CancellationToken ct = default)
    {
        ErrorMessage = null;
        LatestRelease = null;
        InstallerDownloadUrl = null;

        try
        {
            string url = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
            GitHubRelease? release = await _http.GetFromJsonAsync<GitHubRelease>(url, ct);

            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            {
                return false;
            }

            LatestRelease = release;

            ReleaseAsset? asset = release.Assets.FirstOrDefault(a =>
                a.Name.StartsWith("PDownloader.Installer", StringComparison.OrdinalIgnoreCase)
                && a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

            if (asset is not null)
            {
                InstallerDownloadUrl = asset.DownloadUrl;
                InstallerSize = asset.Size;
            }

            return IsNewerVersion(release.TagName);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
    }

    public async Task DownloadInstallerAsync(
        IProgress<double> progress,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(InstallerDownloadUrl))
        {
            throw new InvalidOperationException("No installer URL available.");
        }

        ErrorMessage = null;
        DownloadedInstallerPath = null;

        string tempDir = GetUpdateDirectory();
        PrepareUpdateDirectory(tempDir);

        string fileName = Path.GetFileName(new Uri(InstallerDownloadUrl).LocalPath);
        string destPath = Path.Combine(tempDir, fileName);
        string partialPath = destPath + ".download";

        try
        {
            using HttpResponseMessage response = await _http.GetAsync(
                InstallerDownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                ct);
            response.EnsureSuccessStatusCode();

            long total = response.Content.Headers.ContentLength ?? InstallerSize;

            await using (Stream source = await response.Content.ReadAsStreamAsync(ct))
            await using (var destination = new FileStream(
                partialPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                var buffer = new byte[81920];
                long downloaded = 0;
                int read;

                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                    downloaded += read;

                    if (total > 0)
                    {
                        progress.Report((double)downloaded / total);
                    }
                }

                await destination.FlushAsync(ct);
            }

            File.Move(partialPath, destPath, overwrite: true);
            SavePendingUpdate(tempDir, fileName);
            DownloadedInstallerPath = destPath;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            TryDeleteDirectory(tempDir);
            throw;
        }
    }

    private static void PrepareUpdateDirectory(string tempDirectory)
    {
        TryDeleteDirectory(tempDirectory);
        Directory.CreateDirectory(tempDirectory);
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // A previous installer may still be shutting down. The installer itself
            // also schedules a delayed retry when it exits.
        }
    }

    public bool TryLaunchPendingInstaller()
    {
        ErrorMessage = null;

        if (!TryRestorePendingInstaller())
        {
            return false;
        }

        try
        {
            LaunchInstaller();
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            DownloadedInstallerPath = null;
            return false;
        }
    }

    public void LaunchInstaller()
    {
        if (DownloadedInstallerPath is not string installerPath
            || !File.Exists(installerPath))
        {
            throw new FileNotFoundException("Installer not found.", DownloadedInstallerPath);
        }

        string updateDirectory = Path.GetDirectoryName(installerPath)
            ?? throw new InvalidOperationException("Update directory not found.");

        var startInfo = new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = true,
        };

        // Application updates run without installer UI and reopen PDownloader
        // after the update has completed successfully.
        startInfo.ArgumentList.Add("--silent");
        startInfo.ArgumentList.Add("--launch-after-install");

        // Pass the exact directory to the elevated installer. Inferring it later
        // from Environment.ProcessPath is not reliable for every publish mode.
        startInfo.ArgumentList.Add("--update-temp-dir");
        startInfo.ArgumentList.Add(updateDirectory);

        bool consumedPendingUpdate = ConsumePendingUpdate(updateDirectory);

        try
        {
            using Process installerProcess = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to launch the installer.");
        }
        catch
        {
            if (consumedPendingUpdate)
            {
                try
                {
                    SavePendingUpdate(
                        updateDirectory,
                        Path.GetFileName(installerPath));
                }
                catch
                {
                    // Preserve the original launch failure.
                }
            }

            throw;
        }

        System.Windows.Application.Current.Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown());
    }

    private bool TryRestorePendingInstaller()
    {
        string updateDirectory = GetUpdateDirectory();
        string markerPath = Path.Combine(
            updateDirectory,
            PendingUpdateMarkerName);

        if (!File.Exists(markerPath))
        {
            return false;
        }

        try
        {
            PendingUpdateInfo? pendingUpdate = JsonSerializer.Deserialize<PendingUpdateInfo>(
                File.ReadAllText(markerPath));

            if (pendingUpdate is null
                || !IsSafeInstallerFileName(pendingUpdate.InstallerFileName))
            {
                ClearPendingUpdate(updateDirectory);
                return false;
            }

            string installerPath = Path.Combine(
                updateDirectory,
                pendingUpdate.InstallerFileName);
            if (!File.Exists(installerPath))
            {
                ClearPendingUpdate(updateDirectory);
                return false;
            }

            DownloadedInstallerPath = installerPath;
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            ClearPendingUpdate(updateDirectory);
            return false;
        }
    }

    private static void SavePendingUpdate(
        string updateDirectory,
        string installerFileName)
    {
        if (!IsSafeInstallerFileName(installerFileName))
        {
            throw new InvalidDataException("Invalid installer file name.");
        }

        string markerPath = Path.Combine(
            updateDirectory,
            PendingUpdateMarkerName);
        string temporaryMarkerPath = markerPath + ".tmp";
        string json = JsonSerializer.Serialize(
            new PendingUpdateInfo(installerFileName));

        File.WriteAllText(temporaryMarkerPath, json);
        File.Move(temporaryMarkerPath, markerPath, overwrite: true);
    }

    private static void ClearPendingUpdate(string updateDirectory)
    {
        try
        {
            string markerPath = Path.Combine(
                updateDirectory,
                PendingUpdateMarkerName);
            File.Delete(markerPath);
            File.Delete(markerPath + ".tmp");
        }
        catch
        {
            // The installer also removes the complete update directory after
            // it exits, so marker cleanup here is best-effort.
        }
    }

    private static bool ConsumePendingUpdate(string updateDirectory)
    {
        string markerPath = Path.Combine(
            updateDirectory,
            PendingUpdateMarkerName);
        if (!File.Exists(markerPath))
        {
            return false;
        }

        // Remove the marker before starting the installer so the relaunched
        // application cannot start the same update a second time.
        File.Delete(markerPath);
        File.Delete(markerPath + ".tmp");
        return true;
    }

    private static bool IsSafeInstallerFileName(string fileName) =>
        !string.IsNullOrWhiteSpace(fileName)
        && fileName.Equals(
            Path.GetFileName(fileName),
            StringComparison.Ordinal)
        && fileName.StartsWith(
            "PDownloader.Installer",
            StringComparison.OrdinalIgnoreCase)
        && fileName.EndsWith(
            ".exe",
            StringComparison.OrdinalIgnoreCase);

    private static string GetUpdateDirectory() =>
        Path.Combine(Path.GetTempPath(), UpdateTempDirectoryName);

    public static Version GetCurrentVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
    }

    private static bool IsNewerVersion(string tagName)
    {
        string cleaned = tagName.TrimStart('v', 'V').Split('-')[0];
        if (!Version.TryParse(cleaned, out Version? remote))
        {
            return false;
        }

        Version current = GetCurrentVersion();
        return remote > current;
    }

    private sealed record PendingUpdateInfo(string InstallerFileName);
}
