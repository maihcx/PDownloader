// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
//
// Copyright (C) Song Mai Software.

using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;

namespace PDownloader.Core.Update;

public sealed class CoreUpdateService
{
    private const string GitHubOwner = "maihcx";
    private const string GitHubRepo = "PDownloader";
    private const string UpdateTempDirectoryName = "PDownloaderUpdate";
    private const string PendingUpdateMarkerName = "pending-update.json";
    private const string LegacyX64InstallerFileName = "PDownloader.Installer.exe";
    private const string VersionedX64InstallerFileNamePrefix = "PDownloader.Installer-v";
    private const string X64InstallerFileName = "PDownloader.Installer-win-x64.exe";
    private const string Arm64InstallerFileName = "PDownloader.Installer-win-arm64.exe";
    private const string InstallerFileNameExtension = ".exe";

    private static readonly HttpClient Http = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "PDownloader-Core-Updater" } },
        Timeout = TimeSpan.FromSeconds(30),
    };

    internal GitHubRelease? LatestRelease { get; private set; }
    public string? InstallerDownloadUrl { get; private set; }
    private string? InstallerFileName { get; set; }
    public long InstallerSize { get; private set; }
    public string? DownloadedInstallerPath { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task<bool> CheckForUpdateAsync(CancellationToken ct = default)
    {
        ErrorMessage = null;
        LatestRelease = null;
        InstallerDownloadUrl = null;
        InstallerFileName = null;
        InstallerSize = 0;

        try
        {
            string url = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
            GitHubRelease? release = await Http.GetFromJsonAsync<GitHubRelease>(url, ct);

            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            {
                return false;
            }

            LatestRelease = release;

            if (!IsNewerVersion(release.TagName))
            {
                return false;
            }

            string expectedInstallerFileName = GetExpectedInstallerFileName();
            ReleaseAsset? asset = FindCompatibleInstallerAsset(
                release.Assets,
                expectedInstallerFileName,
                release.TagName);

            if (asset is null)
            {
                throw new InvalidOperationException(
                    $"The release does not contain the compatible installer " +
                    $"'{expectedInstallerFileName}'.");
            }

            InstallerDownloadUrl = asset.DownloadUrl;
            InstallerFileName = asset.Name;
            InstallerSize = asset.Size;
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
    }

    public async Task DownloadInstallerAsync(
        IProgress<double> progress,
        CancellationToken ct = default)
    {
        string? installerDownloadUrl = InstallerDownloadUrl;
        string? installerFileName = InstallerFileName;

        if (string.IsNullOrEmpty(installerDownloadUrl)
            || string.IsNullOrEmpty(installerFileName))
        {
            throw new InvalidOperationException("No installer URL available.");
        }

        ErrorMessage = null;
        DownloadedInstallerPath = null;

        string tempDirectory = GetUpdateDirectory();
        PrepareUpdateDirectory(tempDirectory);

        string fileName = installerFileName;
        string destinationPath = Path.Combine(tempDirectory, fileName);
        string partialPath = destinationPath + ".download";

        try
        {
            using HttpResponseMessage response = await Http.GetAsync(
                installerDownloadUrl,
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

            File.Move(partialPath, destinationPath, overwrite: true);
            SavePendingUpdate(tempDirectory, fileName);
            DownloadedInstallerPath = destinationPath;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            TryDeleteDirectory(tempDirectory);
            throw;
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
            throw new FileNotFoundException(
                "Installer not found.",
                DownloadedInstallerPath);
        }

        string updateDirectory = Path.GetDirectoryName(installerPath)
            ?? throw new InvalidOperationException("Update directory not found.");

        var startInfo = new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = true,
        };

        startInfo.ArgumentList.Add("--silent");
        startInfo.ArgumentList.Add("--launch-after-install");
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
    }

    public UpdateReleaseInfo? CreateReleaseInfo()
    {
        if (LatestRelease is not { } release)
        {
            return null;
        }

        return new UpdateReleaseInfo
        {
            TagName = release.TagName,
            Name = release.Name,
            Body = release.Body,
            HtmlUrl = release.HtmlUrl,
        };
    }

    private bool TryRestorePendingInstaller()
    {
        string updateDirectory = GetUpdateDirectory();
        string markerPath = Path.Combine(updateDirectory, PendingUpdateMarkerName);

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
            // A previous installer may still be shutting down.
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

        string markerPath = Path.Combine(updateDirectory, PendingUpdateMarkerName);
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
            // The installer also removes the complete update directory.
        }
    }

    private static bool ConsumePendingUpdate(string updateDirectory)
    {
        string markerPath = Path.Combine(updateDirectory, PendingUpdateMarkerName);
        if (!File.Exists(markerPath))
        {
            return false;
        }

        File.Delete(markerPath);
        File.Delete(markerPath + ".tmp");
        return true;
    }

    private static ReleaseAsset? FindCompatibleInstallerAsset(
        IEnumerable<ReleaseAsset> assets,
        string expectedInstallerFileName,
        string releaseTagName)
    {
        ReleaseAsset? asset = assets.FirstOrDefault(candidate =>
            candidate.Name.Equals(
                expectedInstallerFileName,
                StringComparison.OrdinalIgnoreCase));

        // The versioned installer is an x64 compatibility alias for clients
        // that predate architecture-specific release assets. Never use it on
        // ARM64 because the file name itself does not identify architecture.
        if (asset is null
            && RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            string? versionedInstallerFileName =
                GetVersionedX64InstallerFileName(releaseTagName);

            if (versionedInstallerFileName is not null)
            {
                asset = assets.FirstOrDefault(candidate =>
                    candidate.Name.Equals(
                        versionedInstallerFileName,
                        StringComparison.OrdinalIgnoreCase));
            }
        }

        // Older releases used the generic name for the x64 installer. Keep
        // that fallback only on x64; its architecture is ambiguous on ARM64.
        if (asset is null
            && RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            asset = assets.FirstOrDefault(candidate =>
                candidate.Name.Equals(
                    LegacyX64InstallerFileName,
                    StringComparison.OrdinalIgnoreCase));
        }

        return asset;
    }

    private static bool IsSafeInstallerFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || !fileName.Equals(
                Path.GetFileName(fileName),
                StringComparison.Ordinal))
        {
            return false;
        }

        if (fileName.Equals(
                GetExpectedInstallerFileName(),
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return RuntimeInformation.ProcessArchitecture == Architecture.X64
            && (fileName.Equals(
                    LegacyX64InstallerFileName,
                    StringComparison.OrdinalIgnoreCase)
                || IsVersionedX64InstallerFileName(fileName));
    }

    private static string? GetVersionedX64InstallerFileName(string tagName)
    {
        string versionText = tagName.TrimStart('v', 'V').Split('-')[0];
        return Version.TryParse(versionText, out _)
            ? $"{VersionedX64InstallerFileNamePrefix}{versionText}{InstallerFileNameExtension}"
            : null;
    }

    private static bool IsVersionedX64InstallerFileName(string fileName)
    {
        if (!fileName.StartsWith(
                VersionedX64InstallerFileNamePrefix,
                StringComparison.OrdinalIgnoreCase)
            || !fileName.EndsWith(
                InstallerFileNameExtension,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int versionLength = fileName.Length
            - VersionedX64InstallerFileNamePrefix.Length
            - InstallerFileNameExtension.Length;
        if (versionLength <= 0)
        {
            return false;
        }

        string versionText = fileName.Substring(
            VersionedX64InstallerFileNamePrefix.Length,
            versionLength);
        return Version.TryParse(versionText, out _);
    }

    private static string GetExpectedInstallerFileName() =>
        RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => X64InstallerFileName,
            Architecture.Arm64 => Arm64InstallerFileName,
            Architecture architecture => throw new PlatformNotSupportedException(
                $"PDownloader updates are not available for {architecture}.")
        };

    private static string GetUpdateDirectory() =>
        Path.Combine(Path.GetTempPath(), UpdateTempDirectoryName);

    private static Version GetCurrentVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version
        ?? new Version(1, 0, 0);

    private static bool IsNewerVersion(string tagName)
    {
        string cleaned = tagName.TrimStart('v', 'V').Split('-')[0];
        return Version.TryParse(cleaned, out Version? remote)
            && remote > GetCurrentVersion();
    }

    private sealed record PendingUpdateInfo(string InstallerFileName);
}
