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

internal sealed class SegmentProgressTracker
{
    private readonly IReadOnlyList<SegmentInfo> _segments;
    private readonly long[] _lastBytes;
    private long _lastTimestamp;

    public SegmentProgressTracker(IReadOnlyList<SegmentInfo> segments)
    {
        _segments = segments;
        _lastBytes = segments
            .OrderBy(segment => segment.Index)
            .Select(segment => Math.Max(0, segment.BytesWritten))
            .ToArray();
        _lastTimestamp = Stopwatch.GetTimestamp();
    }

    public IReadOnlyList<DownloadThreadProgress> Capture()
    {
        SegmentInfo[] ordered = _segments
            .OrderBy(segment => segment.Index)
            .ToArray();

        long now = Stopwatch.GetTimestamp();
        long timestampDelta = now - _lastTimestamp;
        double elapsedSeconds = timestampDelta > 0
            ? timestampDelta / (double)Stopwatch.Frequency
            : 0;

        var snapshots = new DownloadThreadProgress[ordered.Length];

        for (int index = 0; index < ordered.Length; index++)
        {
            SegmentInfo segment = ordered[index];
            long downloadedBytes = Math.Max(0, segment.BytesWritten);
            long previousBytes = index < _lastBytes.Length ? _lastBytes[index] : 0;
            long byteDelta = downloadedBytes - previousBytes;

            long totalBytes = segment.RangeEnd >= 0
                ? Math.Max(0, segment.Length)
                : 0;

            DownloadThreadState state = segment.IsCompleted
                ? DownloadThreadState.Completed
                : segment.TransferState;

            double speedBps = state is DownloadThreadState.Completed or DownloadThreadState.Failed
                ? 0
                : byteDelta > 0 && elapsedSeconds > 0
                    ? byteDelta / elapsedSeconds
                    : 0;

            snapshots[index] = new DownloadThreadProgress(
                segment.Index,
                downloadedBytes,
                totalBytes,
                speedBps,
                state.ToString());

            if (index < _lastBytes.Length)
            {
                _lastBytes[index] = downloadedBytes;
            }
        }

        _lastTimestamp = now;
        return snapshots;
    }
}
