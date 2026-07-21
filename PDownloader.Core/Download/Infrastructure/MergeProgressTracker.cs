// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
//
// Copyright (C) Song Mai Software.

using System.Buffers;

namespace PDownloader.Core.Download.Infrastructure;

/// <summary>
/// Tracks the number of bytes physically written during a merge and publishes
/// a throttled 0..100 percentage. This keeps merge progress tied to real I/O
/// instead of estimating it from elapsed time.
/// </summary>
internal sealed class MergeProgressTracker
{
    private const int CopyBufferSize = 1024 * 1024;
    private static readonly long MinimumReportIntervalTicks =
        Math.Max(1, Stopwatch.Frequency / 10);

    private readonly long _totalBytes;
    private readonly Action<double>? _reportProgress;
    private readonly double _maxProgressBeforeComplete;

    private long _processedBytes;
    private long _lastReportTimestamp;
    private double _lastReportedProgress = -1;

    public MergeProgressTracker(
        long totalBytes,
        Action<double>? reportProgress,
        double maxProgressBeforeComplete = 100)
    {
        _totalBytes = Math.Max(0, totalBytes);
        _reportProgress = reportProgress;
        _maxProgressBeforeComplete = Math.Clamp(
            maxProgressBeforeComplete,
            0,
            100);
    }

    public void Start()
    {
        _processedBytes = 0;
        Publish(0, force: true);
    }

    public async Task CopyToAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);

        try
        {
            while (true)
            {
                int read = await source.ReadAsync(
                    buffer.AsMemory(0, buffer.Length),
                    cancellationToken);

                if (read <= 0)
                {
                    return;
                }

                await destination.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken);

                AddProcessedBytes(read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public void AddProcessedBytes(long byteCount)
    {
        if (byteCount <= 0)
        {
            return;
        }

        long processed = Interlocked.Add(ref _processedBytes, byteCount);
        PublishProcessedBytes(processed);
    }

    public void SetProcessedBytes(long processedBytes)
    {
        processedBytes = Math.Max(0, processedBytes);

        while (true)
        {
            long current = Interlocked.Read(ref _processedBytes);
            if (processedBytes <= current)
            {
                return;
            }

            if (Interlocked.CompareExchange(
                    ref _processedBytes,
                    processedBytes,
                    current) == current)
            {
                PublishProcessedBytes(processedBytes);
                return;
            }
        }
    }

    public void Complete()
    {
        if (_totalBytes > 0)
        {
            Interlocked.Exchange(ref _processedBytes, _totalBytes);
        }

        Publish(100, force: true);
    }

    private void PublishProcessedBytes(long processedBytes)
    {
        double progress = _totalBytes > 0
            ? processedBytes / (double)_totalBytes * 100.0
            : 0;

        progress = Math.Min(
            Math.Clamp(progress, 0, 100),
            _maxProgressBeforeComplete);

        Publish(progress, force: false);
    }

    private void Publish(double progress, bool force)
    {
        if (_reportProgress == null)
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        if (!force)
        {
            if (_lastReportTimestamp != 0
                && now - _lastReportTimestamp < MinimumReportIntervalTicks)
            {
                return;
            }

            if (Math.Abs(progress - _lastReportedProgress) < 0.01)
            {
                return;
            }
        }

        _lastReportTimestamp = now;
        _lastReportedProgress = progress;
        _reportProgress(progress);
    }
}
