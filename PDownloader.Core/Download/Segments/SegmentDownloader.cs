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

namespace PDownloader.Core.Download.Segments;

internal sealed class SegmentDownloader
{
    private const int MaxRetries = 5;
    private const int BufferSize = 81920;
    private const int StallTimeoutSeconds = 20;

    private readonly HttpClient _httpClient;

    public SegmentDownloader(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task DownloadAllAsync(
        IReadOnlyCollection<SegmentInfo> segments,
        bool supportsRange,
        string url,
        CancellationToken cancellationToken)
    {
        IEnumerable<Task> tasks = segments
            .Where(segment => !segment.IsCompleted)
            .Select(segment => DownloadWithRetryAsync(
                segment,
                supportsRange,
                url,
                cancellationToken));

        return Task.WhenAll(tasks);
    }

    private async Task DownloadWithRetryAsync(
        SegmentInfo segment,
        bool supportsRange,
        string url,
        CancellationToken cancellationToken)
    {
        int attempt = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            segment.TransferState = DownloadThreadState.Downloading;

            try
            {
                await DownloadSegmentAsync(
                    segment,
                    supportsRange,
                    url,
                    cancellationToken);
                segment.TransferState = DownloadThreadState.Completed;
                segment.RetryAttempt = 0;
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (RangeRejectedException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < MaxRetries)
            {
                attempt++;
                segment.TransferState = DownloadThreadState.Retrying;
                segment.RetryAttempt = attempt;

                int delayMilliseconds = (int)Math.Pow(2, attempt) * 500;
                Debug.WriteLine(
                    $"[Segments] Segment {segment.Index}, lần thử {attempt} thất bại: " +
                    $"{ex.Message}. Thử lại sau {delayMilliseconds}ms.");
                await Task.Delay(delayMilliseconds, cancellationToken);
            }
            catch
            {
                segment.TransferState = DownloadThreadState.Failed;
                throw;
            }
        }
    }

    private async Task DownloadSegmentAsync(
        SegmentInfo segment,
        bool supportsRange,
        string url,
        CancellationToken cancellationToken)
    {
        SynchronizeLengthWithFile(segment);

        long expectedLength = GetExpectedLength(segment);
        if (expectedLength > 0 && segment.BytesWritten >= expectedLength)
        {
            if (segment.BytesWritten > expectedLength)
            {
                throw new InvalidDataException(
                    $"Segment {segment.Index} lớn hơn kích thước dự kiến: " +
                    $"{segment.BytesWritten}/{expectedLength} byte.");
            }

            segment.IsCompleted = true;
            segment.TransferState = DownloadThreadState.Completed;
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        long resumeFrom = segment.RangeStart + segment.BytesWritten;
        bool shouldSetRange = supportsRange && segment.RangeEnd >= 0;

        if (shouldSetRange)
        {
            request.Headers.Range = new RangeHeaderValue(resumeFrom, segment.RangeEnd);
        }

        using var stallCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ResetStallTimeout(stallCancellation);

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            stallCancellation.Token);
        ResetStallTimeout(stallCancellation);

        HandleRangeResponse(segment, response, resumeFrom, shouldSetRange);
        if (segment.IsCompleted)
        {
            return;
        }

        response.EnsureSuccessStatusCode();

        string? contentType = response.Content.Headers.ContentType?.MediaType;
        if (DownloadContentInspector.IsHtmlContentType(contentType))
        {
            throw new InvalidOperationException(
                "Server trả về trang HTML thay vì file. " +
                "Có thể URL yêu cầu đăng nhập hoặc đã hết hạn.");
        }

        bool serverHonoredRange = response.StatusCode == HttpStatusCode.PartialContent;
        FileMode mode = segment.BytesWritten > 0 && serverHonoredRange
            ? FileMode.Append
            : FileMode.Create;

        if (mode == FileMode.Create)
        {
            segment.BytesWritten = 0;
        }

        string? directory = Path.GetDirectoryName(segment.TempFilePath);
        if (directory != null)
        {
            Directory.CreateDirectory(directory);
        }

        await using var output = new FileStream(
            segment.TempFilePath,
            mode,
            FileAccess.Write,
            FileShare.None);
        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);

        byte[] buffer = new byte[BufferSize];
        int firstRead = await input.ReadAsync(buffer.AsMemory(), stallCancellation.Token);
        ResetStallTimeout(stallCancellation);

        if (firstRead > 0 && segment.BytesWritten == 0 && segment.Index == 0
            && DownloadContentInspector.LooksLikeHtml(buffer, firstRead))
        {
            throw new InvalidOperationException(
                "Nội dung tải về là trang HTML (trang lỗi hoặc yêu cầu đăng nhập), " +
                "không phải file thật.");
        }

        if (firstRead > 0)
        {
            await WriteBufferAsync(
                output,
                buffer,
                firstRead,
                segment,
                cancellationToken);
        }

        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(), stallCancellation.Token)) > 0)
        {
            ResetStallTimeout(stallCancellation);
            await WriteBufferAsync(
                output,
                buffer,
                read,
                segment,
                cancellationToken);
        }

        ValidateCompletedLength(segment);
        segment.IsCompleted = true;
    }

    private static void HandleRangeResponse(
        SegmentInfo segment,
        HttpResponseMessage response,
        long resumeFrom,
        bool rangeWasRequested)
    {
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            long expectedBytes = GetExpectedLength(segment);
            if (expectedBytes > 0 && segment.BytesWritten >= expectedBytes)
            {
                segment.IsCompleted = true;
                segment.TransferState = DownloadThreadState.Completed;
                return;
            }

            throw new HttpRequestException(
                $"Server trả 416 cho segment {segment.Index} " +
                $"(range {resumeFrom}-{segment.RangeEnd}), đã có {segment.BytesWritten}B.");
        }

        if (!rangeWasRequested)
        {
            return;
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new RangeRejectedException(
                $"Server trả 403 khi request có Range cho segment {segment.Index}.");
        }

        if (response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.PartialContent)
        {
            throw new RangeRejectedException(
                $"Server không hỗ trợ Range ổn định cho segment {segment.Index} " +
                $"(trả về {(int)response.StatusCode} thay vì 206).");
        }

        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            ContentRangeHeaderValue? contentRange = response.Content.Headers.ContentRange;
            if (contentRange?.From != resumeFrom)
            {
                string actualRange = contentRange == null
                    ? "không có Content-Range"
                    : contentRange.ToString();

                throw new RangeRejectedException(
                    $"Server trả sai range cho segment {segment.Index}: " +
                    $"yêu cầu bắt đầu từ {resumeFrom}, nhận {actualRange}.");
            }

            if (segment.RangeEnd >= 0
                && contentRange.To.HasValue
                && contentRange.To.Value > segment.RangeEnd)
            {
                throw new RangeRejectedException(
                    $"Server trả vượt range của segment {segment.Index}: " +
                    $"{contentRange} (tối đa {segment.RangeEnd}).");
            }
        }
    }

    private static async Task WriteBufferAsync(
        FileStream output,
        byte[] buffer,
        int count,
        SegmentInfo segment,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        segment.BytesWritten += count;
    }

    private static void SynchronizeLengthWithFile(SegmentInfo segment)
    {
        long actualLength = File.Exists(segment.TempFilePath)
            ? new FileInfo(segment.TempFilePath).Length
            : 0;

        if (actualLength != segment.BytesWritten)
        {
            segment.BytesWritten = actualLength;
        }
    }

    private static void ValidateCompletedLength(SegmentInfo segment)
    {
        long expectedLength = GetExpectedLength(segment);
        if (expectedLength <= 0 || segment.BytesWritten == expectedLength)
        {
            return;
        }

        string reason = segment.BytesWritten < expectedLength
            ? "bị thiếu dữ liệu"
            : "có dữ liệu vượt quá range";

        throw new InvalidDataException(
            $"Segment {segment.Index} {reason}: " +
            $"đã tải {segment.BytesWritten}/{expectedLength} byte.");
    }

    private static long GetExpectedLength(SegmentInfo segment) =>
        segment.RangeEnd >= 0
            ? segment.RangeEnd - segment.RangeStart + 1
            : -1;

    private static void ResetStallTimeout(CancellationTokenSource source) =>
        source.CancelAfter(TimeSpan.FromSeconds(StallTimeoutSeconds));
}
