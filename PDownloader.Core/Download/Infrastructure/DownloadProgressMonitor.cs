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

namespace PDownloader.Core.Download.Infrastructure;

internal sealed class DownloadProgressMonitor : IDisposable
{
    private readonly Func<long> _getDownloadedBytes;
    private readonly Action<long, double> _report;
    private readonly Action? _afterReport;
    private readonly System.Timers.Timer _timer;
    private readonly object _sync = new();

    private long _lastReportedBytes;
    private long _lastSampleTimestamp;
    private int _isTicking;
    private bool _isRunning;
    private bool _disposed;

    public DownloadProgressMonitor(
        Func<long> getDownloadedBytes,
        Action<long, double> report,
        Action? afterReport = null,
        double intervalMilliseconds = 1000)
    {
        ArgumentNullException.ThrowIfNull(getDownloadedBytes);
        ArgumentNullException.ThrowIfNull(report);

        if (intervalMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(intervalMilliseconds),
                intervalMilliseconds,
                "The progress update interval must be greater than 0.");
        }

        _getDownloadedBytes = getDownloadedBytes;
        _report = report;
        _afterReport = afterReport;

        _timer = new System.Timers.Timer(intervalMilliseconds)
        {
            AutoReset = true,
        };
        _timer.Elapsed += OnElapsed;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_sync)
        {
            _lastReportedBytes = _getDownloadedBytes();
            _lastSampleTimestamp = Stopwatch.GetTimestamp();
            _isRunning = true;
        }

        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();

        lock (_sync)
        {
            _isRunning = false;
        }
    }

    public void ReportFinal(double speedBps = 0)
    {
        _timer.Stop();

        lock (_sync)
        {
            _isRunning = false;

            long current = _getDownloadedBytes();
            _lastReportedBytes = current;
            _lastSampleTimestamp = Stopwatch.GetTimestamp();

            _report(current, Math.Max(0, speedBps));
            _afterReport?.Invoke();
        }
    }

    private void OnElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (Interlocked.Exchange(ref _isTicking, 1) != 0)
        {
            return;
        }

        try
        {
            lock (_sync)
            {
                if (!_isRunning || _disposed)
                {
                    return;
                }

                long current = _getDownloadedBytes();
                long now = Stopwatch.GetTimestamp();

                long byteDelta = current - _lastReportedBytes;
                long timestampDelta = now - _lastSampleTimestamp;

                _lastReportedBytes = current;
                _lastSampleTimestamp = now;

                double elapsedSeconds = timestampDelta / (double)Stopwatch.Frequency;
                double speedBps = byteDelta > 0 && elapsedSeconds > 0
                    ? byteDelta / elapsedSeconds
                    : 0;

                _report(current, speedBps);
                _afterReport?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Progress] Unable to update progress: {ex.Message}");
        }
        finally
        {
            Volatile.Write(ref _isTicking, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _timer.Stop();

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _isRunning = false;
            _disposed = true;
        }

        _timer.Elapsed -= OnElapsed;
        _timer.Dispose();
    }
}
