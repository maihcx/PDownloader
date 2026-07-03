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
using System.Reflection;
using System.Text.Json.Serialization;

namespace PDownloader.Tray.Services;

public static class UpdateChecker
{
    private const string GitHubOwner = "maihcx";
    private const string GitHubRepo = "PDownloader";

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
            GitHubReleaseDto? release = await _http.GetFromJsonAsync<GitHubReleaseDto>(url, ct);

            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            {
                return null;
            }

            if (!IsNewerVersion(release.TagName))
            {
                return null;
            }

            return new TrayReleaseInfo
            {
                TagName = release.TagName,
                HtmlUrl = release.HtmlUrl,
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
        if (!Version.TryParse(cleaned, out Version? remote))
        {
            return false;
        }

        Version current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
        return remote > current;
    }

    private sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = string.Empty;
    }
}
