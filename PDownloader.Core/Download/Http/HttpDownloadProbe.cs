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

using System.Net.Http.Headers;

namespace PDownloader.Core.Download;

internal sealed class HttpDownloadProbe
{
    private readonly HttpClient _httpClient;

    public HttpDownloadProbe(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<DownloadProbeResult> ProbeAsync(
        string url,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            bool headIsUnreliable = !response.IsSuccessStatusCode
                || response.Content.Headers.ContentLength is null or 0;

            if (headIsUnreliable)
            {
                return await ProbeViaRangedGetAsync(url, cancellationToken);
            }

            long totalBytes = response.Content.Headers.ContentLength ?? 0;
            bool supportsRange = response.Headers.AcceptRanges.Contains("bytes");
            string fileName = ResolveFileName(response, url);

            return new DownloadProbeResult(totalBytes, supportsRange, fileName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            try
            {
                return await ProbeViaRangedGetAsync(url, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return new DownloadProbeResult(
                    TotalBytes: 0,
                    SupportsRange: false,
                    SuggestedFileName: DownloadPathService.SanitizeFileName(
                        DownloadPathService.GuessFileName(url)));
            }
        }
    }

    public static async Task<string?> GetRemoteFileNameAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        var probe = new HttpDownloadProbe(DownloadHttpClientFactory.GetSharedClient());
        DownloadProbeResult result = await probe.ProbeAsync(url, cancellationToken);
        return result.SuggestedFileName;
    }

    private async Task<DownloadProbeResult> ProbeViaRangedGetAsync(
        string url,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(0, 0);

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        bool supportsRange = response.StatusCode == HttpStatusCode.PartialContent;
        long totalBytes = response.Content.Headers.ContentRange?.Length
            ?? response.Content.Headers.ContentLength
            ?? 0;
        string fileName = ResolveFileName(response, url);

        return new DownloadProbeResult(totalBytes, supportsRange, fileName);
    }

    private static string ResolveFileName(HttpResponseMessage response, string fallbackUrl)
    {
        ContentDispositionHeaderValue? contentDisposition =
            response.Content.Headers.ContentDisposition;

        string name = contentDisposition?.FileNameStar
            ?? contentDisposition?.FileName
            ?? DownloadPathService.GuessFileName(
                response.RequestMessage?.RequestUri?.ToString() ?? fallbackUrl);

        return DownloadPathService.SanitizeFileName(name);
    }
}
