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

namespace PDownloader.Infrastructure.Downloads;

internal static class DownloadHttpClientFactory
{
    private const string DefaultUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36";

    private static readonly HttpClient SharedClient = CreateClient(useCookies: true);

    public static DownloadHttpClientLease Create(Dictionary<string, string>? customHeaders)
    {
        if (customHeaders == null || customHeaders.Count == 0)
        {
            return new DownloadHttpClientLease(SharedClient, ownsClient: false);
        }

        HttpClient client = CreateClient(useCookies: false);
        ApplyHeaders(client, customHeaders);
        return new DownloadHttpClientLease(client, ownsClient: true);
    }

    public static HttpClient GetSharedClient() => SharedClient;

    private static HttpClient CreateClient(bool useCookies)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            UseCookies = useCookies,
        };

        var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(DefaultUserAgent);
        return client;
    }

    private static void ApplyHeaders(HttpClient client, Dictionary<string, string> headers)
    {
        foreach ((string key, string value) in headers)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            try
            {
                switch (key.ToLowerInvariant())
                {
                    case "cookie":
                        client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", value);
                        break;
                    case "referer":
                        client.DefaultRequestHeaders.Referrer = new Uri(value, UriKind.RelativeOrAbsolute);
                        break;
                    case "user-agent":
                        client.DefaultRequestHeaders.UserAgent.Clear();
                        client.DefaultRequestHeaders.UserAgent.ParseAdd(value);
                        break;
                    case "x-pdownloader-cookie-jar":
                        // Internal bridge metadata used only to build yt-dlp's
                        // Netscape cookie jar. Never forward it to remote servers.
                        break;
                    default:
                        client.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HTTP] Bỏ qua header '{key}': {ex.Message}");
            }
        }
    }
}

internal readonly struct DownloadHttpClientLease : IDisposable
{
    private readonly bool _ownsClient;

    public DownloadHttpClientLease(HttpClient client, bool ownsClient)
    {
        Client = client;
        _ownsClient = ownsClient;
    }

    public HttpClient Client { get; }

    public void Dispose()
    {
        if (_ownsClient)
        {
            Client.Dispose();
        }
    }
}
