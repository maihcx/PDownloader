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

namespace PDownloader.Core.Download.Segments;

internal sealed class MultiSegmentDownloadService
{
    private const long MinSizeForMultiSegment = 5 * 1024 * 1024;

    private readonly HttpDownloadProbe _probe;
    private readonly SegmentStateStore _stateStore;
    private readonly SegmentDownloader _segmentDownloader;
    private readonly SegmentMerger _segmentMerger;

    public MultiSegmentDownloadService(HttpClient httpClient)
    {
        _probe = new HttpDownloadProbe(httpClient);
        _stateStore = new SegmentStateStore();
        _segmentDownloader = new SegmentDownloader(httpClient);
        _segmentMerger = new SegmentMerger();
    }

    public Task<DownloadProbeResult> ProbeAsync(
        string url,
        CancellationToken cancellationToken) =>
        _probe.ProbeAsync(url, cancellationToken);

    public async Task<DownloadProbeResult> ProbeAndDownloadAsync(
        string url,
        string destinationPath,
        string tempDirectory,
        int preferredThreadCount,
        long progressBaseOffset,
        Action<long, double> reportProgress,
        Action<IReadOnlyList<DownloadThreadProgress>>? reportThreadProgress,
        CancellationToken cancellationToken)
    {
        DownloadProbeResult probe = await _probe.ProbeAsync(url, cancellationToken);
        await DownloadAsync(
            url,
            destinationPath,
            tempDirectory,
            probe,
            preferredThreadCount,
            progressBaseOffset,
            reportProgress: reportProgress,
            reportThreadProgress: reportThreadProgress,
            mergingStarted: null,
            cancellationToken: cancellationToken);
        return probe;
    }

    public async Task DownloadAsync(
        string url,
        string destinationPath,
        string tempDirectory,
        DownloadProbeResult probe,
        int preferredThreadCount,
        long progressBaseOffset,
        Action<long, double> reportProgress,
        Action<IReadOnlyList<DownloadThreadProgress>>? reportThreadProgress,
        Action? mergingStarted,
        CancellationToken cancellationToken)
    {
        bool useMultipleSegments = probe.SupportsRange
            && probe.TotalBytes >= MinSizeForMultiSegment;
        int threadCount = useMultipleSegments
            ? Math.Max(1, preferredThreadCount)
            : 1;

        List<SegmentInfo> segments = _stateStore.BuildOrRestore(
            tempDirectory,
            probe.TotalBytes,
            threadCount);

        try
        {
            await DownloadAttemptAsync(
                url,
                tempDirectory,
                segments,
                probe.SupportsRange,
                progressBaseOffset,
                reportProgress,
                reportThreadProgress,
                cancellationToken);
        }
        catch (RangeRejectedException ex)
        {
            Debug.WriteLine(
                $"[Segments] Range không ổn định, chuyển về tải một luồng: {ex.Message}");

            _stateStore.Reset(tempDirectory);
            segments = _stateStore.BuildOrRestore(
                tempDirectory,
                probe.TotalBytes,
                threadCount: 1);

            await DownloadAttemptAsync(
                url,
                tempDirectory,
                segments,
                supportsRange: false,
                progressBaseOffset: progressBaseOffset,
                reportProgress: reportProgress,
                reportThreadProgress: reportThreadProgress,
                cancellationToken: cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        mergingStarted?.Invoke();
        await _segmentMerger.MergeAsync(segments, destinationPath, cancellationToken);

        long finalLength = File.Exists(destinationPath)
            ? new FileInfo(destinationPath).Length
            : segments.Sum(segment => segment.BytesWritten);
        reportProgress(progressBaseOffset + finalLength, 0);
    }

    private async Task DownloadAttemptAsync(
        string url,
        string tempDirectory,
        List<SegmentInfo> segments,
        bool supportsRange,
        long progressBaseOffset,
        Action<long, double> reportProgress,
        Action<IReadOnlyList<DownloadThreadProgress>>? reportThreadProgress,
        CancellationToken cancellationToken)
    {
        long GetDownloadedBytes() => segments.Sum(segment => segment.BytesWritten);

        var threadTracker = new SegmentProgressTracker(segments);

        void PublishProgress(long downloaded, double speed)
        {
            reportThreadProgress?.Invoke(threadTracker.Capture());
            reportProgress(progressBaseOffset + downloaded, speed);
        }

        using var monitor = new DownloadProgressMonitor(
            GetDownloadedBytes,
            PublishProgress,
            () => _stateStore.Persist(tempDirectory, segments));

        PublishProgress(GetDownloadedBytes(), 0);
        monitor.Start();
        try
        {
            await _segmentDownloader.DownloadAllAsync(
                segments,
                supportsRange,
                url,
                cancellationToken);
            monitor.ReportFinal();
        }
        finally
        {
            monitor.Stop();
            _stateStore.Persist(tempDirectory, segments);
        }
    }
}
