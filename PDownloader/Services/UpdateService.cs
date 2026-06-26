using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;

namespace PDownloader.Services
{
    public enum UpdateStatus
    {
        Idle,
        Checking,
        UpdateAvailable,
        Downloading,
        ReadyToInstall,
        UpToDate,
        Error,
    }

    public class ReleaseAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string DownloadUrl { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    public class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = string.Empty;

        [JsonPropertyName("assets")]
        public List<ReleaseAsset> Assets { get; set; } = [];
    }

    public class UpdateService
    {
        private const string GitHubOwner = "maihcx";
        private const string GitHubRepo  = "PDownloader";

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
                var release = await _http.GetFromJsonAsync<GitHubRelease>(url, ct);

                if (release is null || string.IsNullOrWhiteSpace(release.TagName))
                    return false;

                LatestRelease = release;

                // Tìm asset installer (.exe)
                var asset = release.Assets.FirstOrDefault(a =>
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
                throw new InvalidOperationException("No installer URL available.");

            ErrorMessage = null;

            string tempDir  = Path.Combine(Path.GetTempPath(), "PDownloaderUpdate");
            Directory.CreateDirectory(tempDir);

            string fileName = Path.GetFileName(new Uri(InstallerDownloadUrl).LocalPath);
            string destPath = Path.Combine(tempDir, fileName);

            try
            {
                using var response = await _http.GetAsync(
                    InstallerDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                long total = response.Content.Headers.ContentLength ?? InstallerSize;

                await using var src  = await response.Content.ReadAsStreamAsync(ct);
                await using var dest = File.Create(destPath);

                var buffer = new byte[81920];
                long downloaded = 0;
                int  read;

                while ((read = await src.ReadAsync(buffer, ct)) > 0)
                {
                    await dest.WriteAsync(buffer.AsMemory(0, read), ct);
                    downloaded += read;
                    if (total > 0)
                        progress.Report((double)downloaded / total);
                }

                DownloadedInstallerPath = destPath;
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                throw;
            }
        }

        /// <summary>
        /// Khởi chạy installer đã tải và tắt ứng dụng hiện tại.
        /// </summary>
        public void LaunchInstaller()
        {
            if (ConfluxManager.cfsPDownloaderCore!.IsAppStarted())
            {
                ConfluxManager.cfsPDownloaderCore.Send("main-event", "OnAppExit");
            }

            if (string.IsNullOrEmpty(DownloadedInstallerPath) || !File.Exists(DownloadedInstallerPath))
                throw new FileNotFoundException("Installer not found.", DownloadedInstallerPath);

            Process.Start(new ProcessStartInfo
            {
                FileName        = DownloadedInstallerPath,
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
            if (!Version.TryParse(cleaned, out var remote))
                return false;

            var current = GetCurrentVersion();
            return remote > current;
        }
    }
}
