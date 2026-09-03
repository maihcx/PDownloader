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

using System.Net;
using System.Net.Http.Headers;

namespace PDownloader.Downloads.Segments;

internal sealed class SegmentDownloader
{
    private const int MaxRetries = 5;
    private const int BufferSize = 81920;
    private const int StallTimeoutSeconds = 20;
    private static readonly TimeSpan CheckpointInterval = TimeSpan.FromSeconds(2);

    private readonly HttpClient _httpClient;

    public SegmentDownloader(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task DownloadAllAsync(
        IReadOnlyCollection<SegmentInfo> segments,
        bool supportsRange,
        string url,
        Action persistResetCheckpoint,
        CancellationToken cancellationToken)
    {
        IEnumerable<Task> tasks = segments
            .Where(segment => !segment.IsCompleted)
            .Select(segment => DownloadWithRetryAsync(
                segment,
                supportsRange,
                url,
                persistResetCheckpoint,
                cancellationToken));

        return Task.WhenAll(tasks);
    }

    private async Task DownloadWithRetryAsync(
        SegmentInfo segment,
        bool supportsRange,
        string url,
        Action persistResetCheckpoint,
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
                    persistResetCheckpoint,
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
                    $"[Segments] Segment {segment.Index}, attempt {attempt} failed: " +
                    $"{ex.Message}. Try again after {delayMilliseconds}ms.");
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
        Action persistResetCheckpoint,
        CancellationToken cancellationToken)
    {
        SynchronizeLengthWithFile(segment);

        long expectedLength = GetExpectedLength(segment);
        if (expectedLength > 0 && segment.BytesWritten >= expectedLength)
        {
            if (segment.BytesWritten > expectedLength)
            {
                throw new InvalidDataException(
                    $"Segment {segment.Index} is larger than the expected size: " +
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
                "The server returned an HTML page instead of the file. " +
                "The requested URL might require a login or have expired.");
        }

        bool serverHonoredRange = response.StatusCode == HttpStatusCode.PartialContent;
        FileMode mode = segment.BytesWritten > 0 && serverHonoredRange
            ? FileMode.Append
            : FileMode.Create;

        if (mode == FileMode.Create)
        {
            SegmentInfo previousCheckpoint = segment.CaptureCheckpoint();
            segment.BytesWritten = 0;
            segment.IsCompleted = false;
            segment.CommitCheckpoint(0, false);
            try
            {
                if (previousCheckpoint.BytesWritten > 0)
                {
                    persistResetCheckpoint();
                }
            }
            catch
            {
                // The part has not been truncated. Do not let a retry truncate
                // it using a reset checkpoint whose commit failed.
                segment.BytesWritten = previousCheckpoint.BytesWritten;
                segment.IsCompleted = previousCheckpoint.IsCompleted;
                segment.CommitCheckpoint(previousCheckpoint.BytesWritten, previousCheckpoint.IsCompleted);
                throw;
            }
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
        bool completed = false;
        long lastCheckpointTimestamp = Stopwatch.GetTimestamp();
        try
        {
            await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
            byte[] buffer = new byte[BufferSize];
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(), stallCancellation.Token)) > 0)
            {
                ResetStallTimeout(stallCancellation);
                if (segment.BytesWritten == 0 && segment.Index == 0
                    && DownloadContentInspector.LooksLikeHtml(buffer, read))
                {
                    throw new InvalidOperationException(
                        "The downloaded content is an HTML page, not the actual file.");
                }

                await WriteBufferAsync(output, buffer, read, segment, cancellationToken);
                if (Stopwatch.GetElapsedTime(lastCheckpointTimestamp) >= CheckpointInterval)
                {
                    FlushCheckpoint(output, segment, completed: false);
                    lastCheckpointTimestamp = Stopwatch.GetTimestamp();
                }
            }

            ValidateCompletedLength(segment);
            completed = true;
        }
        finally
        {
            // No canceled token here: pause/shutdown must finish committing data
            // before the state store publishes its final metadata snapshot.
            FlushCheckpoint(output, segment, completed);
        }

        segment.IsCompleted = true;
    }

    private static void FlushCheckpoint(FileStream output, SegmentInfo segment, bool completed)
    {
        output.Flush(flushToDisk: true);
        segment.CommitCheckpoint(segment.BytesWritten, completed);
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
                $"The server returns a 416 for the segment. {segment.Index} " +
                $"(range {resumeFrom}-{segment.RangeEnd}), {segment.BytesWritten}B written.");
        }

        if (!rangeWasRequested)
        {
            return;
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new RangeRejectedException(
                $"The server returns a 403 error when a request includes a Range header for a segment {segment.Index}.");
        }

        if (response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.PartialContent)
        {
            throw new RangeRejectedException(
                $"The server does not support a stable Range for the segment. {segment.Index} " +
                $"(returns {(int)response.StatusCode} instead of 206).");
        }

        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            ContentRangeHeaderValue? contentRange = response.Content.Headers.ContentRange;
            if (contentRange?.From != resumeFrom)
            {
                string actualRange = contentRange == null
                    ? "Content-Range is missing"
                    : contentRange.ToString();

                throw new RangeRejectedException(
                    $"The server returned an incorrect range for the segment {segment.Index}: " +
                    $"Request starting from {resumeFrom}, receiving {actualRange}.");
            }

            if (segment.RangeEnd >= 0
                && contentRange.To.HasValue
                && contentRange.To.Value > segment.RangeEnd)
            {
                throw new RangeRejectedException(
                    $"The server returned a value exceeding the segment range {segment.Index}: " +
                    $"{contentRange} (maximum {segment.RangeEnd}).");
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
        long actualLength;
        bool exists = true;
        try { actualLength = new FileInfo(segment.TempFilePath).Length; }
        catch (FileNotFoundException) { actualLength = 0; exists = false; }
        catch (DirectoryNotFoundException) { actualLength = 0; exists = false; }

        SegmentInfo checkpoint = segment.CaptureCheckpoint();
        long committedLength = Math.Min(actualLength, checkpoint.BytesWritten);
        if (actualLength > committedLength)
        {
            using var part = new FileStream(segment.TempFilePath, FileMode.Open,
                FileAccess.Write, FileShare.None);
            part.SetLength(committedLength);
            part.Flush(flushToDisk: true);
        }

        segment.BytesWritten = committedLength;
        segment.IsCompleted = exists && checkpoint.IsCompleted && actualLength >= checkpoint.BytesWritten;
        segment.CommitCheckpoint(committedLength, segment.IsCompleted);
    }

    private static void ValidateCompletedLength(SegmentInfo segment)
    {
        long expectedLength = GetExpectedLength(segment);
        if (expectedLength <= 0 || segment.BytesWritten == expectedLength)
        {
            return;
        }

        string reason = segment.BytesWritten < expectedLength
            ? "missing data"
            : "There is data exceeding the range.";

        throw new InvalidDataException(
            $"Segment {segment.Index} {reason}: " +
            $"Downloaded {segment.BytesWritten}/{expectedLength} bytes.");
    }

    private static long GetExpectedLength(SegmentInfo segment) =>
        segment.RangeEnd >= 0
            ? segment.RangeEnd - segment.RangeStart + 1
            : -1;

    private static void ResetStallTimeout(CancellationTokenSource source) =>
        source.CancelAfter(TimeSpan.FromSeconds(StallTimeoutSeconds));
}
