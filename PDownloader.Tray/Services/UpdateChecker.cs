using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PDownloader.Tray.Services;

public class TrayReleaseInfo
{
    public string TagName    { get; init; } = string.Empty;
    public string HtmlUrl    { get; init; } = string.Empty;
    public string ReleaseName { get; init; } = string.Empty;
}

public static class UpdateChecker
{
    private const string GitHubOwner = "maihcx";
    private const string GitHubRepo  = "PDownloader";

    private static readonly HttpClient _http = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "PDownloader-Tray-Updater" } },
        Timeout = TimeSpan.FromSeconds(20),
    };

    public static async Task<TrayReleaseInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            string url = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
            var release = await _http.GetFromJsonAsync<GitHubReleaseDto>(url, ct);

            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
                return null;

            if (!IsNewerVersion(release.TagName))
                return null;

            return new TrayReleaseInfo
            {
                TagName     = release.TagName,
                HtmlUrl     = release.HtmlUrl,
                ReleaseName = release.Name,
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool IsNewerVersion(string tagName)
    {
        string cleaned = tagName.TrimStart('v', 'V').Split('-')[0];
        if (!Version.TryParse(cleaned, out var remote))
            return false;

        var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
        return remote > current;
    }

    private sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")] public string TagName  { get; set; } = string.Empty;
        [JsonPropertyName("name")]     public string Name     { get; set; } = string.Empty;
        [JsonPropertyName("html_url")] public string HtmlUrl  { get; set; } = string.Empty;
    }
}
