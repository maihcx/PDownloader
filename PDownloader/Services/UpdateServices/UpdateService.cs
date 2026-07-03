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

        string tempDir = Path.Combine(Path.GetTempPath(), "PDownloaderUpdate");
        Directory.CreateDirectory(tempDir);

        string fileName = Path.GetFileName(new Uri(InstallerDownloadUrl).LocalPath);
        string destPath = Path.Combine(tempDir, fileName);

        try
        {
            using HttpResponseMessage response = await _http.GetAsync(
                InstallerDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            long total = response.Content.Headers.ContentLength ?? InstallerSize;

            await using Stream src = await response.Content.ReadAsStreamAsync(ct);
            await using FileStream dest = File.Create(destPath);

            var buffer = new byte[81920];
            long downloaded = 0;
            int read;

            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dest.WriteAsync(buffer.AsMemory(0, read), ct);
                downloaded += read;
                if (total > 0)
                {
                    progress.Report((double)downloaded / total);
                }
            }

            DownloadedInstallerPath = destPath;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
    }

    public void LaunchInstaller()
    {
        if (string.IsNullOrEmpty(DownloadedInstallerPath) || !File.Exists(DownloadedInstallerPath))
        {
            throw new FileNotFoundException("Installer not found.", DownloadedInstallerPath);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = DownloadedInstallerPath,
            UseShellExecute = true,
        });

        System.Windows.Application.Current.Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown());
    }

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
}
