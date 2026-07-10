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
    private long _lastReportedBytes;
    private int _isTicking;
    private bool _disposed;

    public DownloadProgressMonitor(
        Func<long> getDownloadedBytes,
        Action<long, double> report,
        Action? afterReport = null,
        double intervalMilliseconds = 1000)
    {
        _getDownloadedBytes = getDownloadedBytes;
        _report = report;
        _afterReport = afterReport;
        _lastReportedBytes = getDownloadedBytes();

        _timer = new System.Timers.Timer(intervalMilliseconds)
        {
            AutoReset = true,
        };
        _timer.Elapsed += OnElapsed;
    }

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    public void ReportFinal(double speedBps = 0)
    {
        _timer.Stop();
        long current = _getDownloadedBytes();
        Interlocked.Exchange(ref _lastReportedBytes, current);
        _report(current, speedBps);
        _afterReport?.Invoke();
    }

    private void OnElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (Interlocked.Exchange(ref _isTicking, 1) != 0)
        {
            return;
        }

        try
        {
            long current = _getDownloadedBytes();
            long previous = Interlocked.Exchange(ref _lastReportedBytes, current);
            double speed = Math.Max(0, current - previous);
            _report(current, speed);
            _afterReport?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Progress] Không thể cập nhật tiến độ: {ex.Message}");
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

        _disposed = true;
        _timer.Stop();
        _timer.Elapsed -= OnElapsed;
        _timer.Dispose();
    }
}
