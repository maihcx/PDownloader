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

namespace PDownloader.Core.Download.Hls;

internal sealed class HlsPlaylistDetector
{
    private const int SniffLength = 32;
    private readonly HttpClient _httpClient;

    public HlsPlaylistDetector(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<bool> IsHlsPlaylistAsync(
        string url,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(0, SniffLength - 1);

            using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            byte[] buffer = new byte[SniffLength];
            int read = 0;
            while (read < buffer.Length)
            {
                int count = await stream.ReadAsync(
                    buffer.AsMemory(read, buffer.Length - read),
                    cancellationToken);
                if (count == 0)
                {
                    break;
                }

                read += count;
            }

            string prefix = Encoding.ASCII.GetString(buffer, 0, read).TrimStart();

            return prefix.StartsWith("#EXTM3U", StringComparison.Ordinal);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }
}
