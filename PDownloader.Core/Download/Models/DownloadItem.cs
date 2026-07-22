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

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PDownloader.Core.Download.Models;

public class DownloadItem : INotifyPropertyChanged
{
    private DownloadThreadProgress[] _threadProgress = Array.Empty<DownloadThreadProgress>();

    public string ProgressVisualizationMode { get; private set; } = "None";

    public string ProgressVisualizationStage { get; private set; } = string.Empty;

    public IReadOnlyList<DownloadThreadProgress> GetThreadProgressSnapshot() =>
        Volatile.Read(ref _threadProgress);

    public void SetThreadProgress(
        string stage,
        IReadOnlyCollection<DownloadThreadProgress> progress)
    {
        ProgressVisualizationStage = stage;
        ProgressVisualizationMode = "Threads";
        Volatile.Write(ref _threadProgress, progress.ToArray());
    }

    public void SetProgressVisualizationUnsupported(string stage)
    {
        ProgressVisualizationStage = stage;
        ProgressVisualizationMode = "Unsupported";
        Volatile.Write(ref _threadProgress, Array.Empty<DownloadThreadProgress>());
    }

    public void ClearProgressVisualization()
    {
        ProgressVisualizationStage = string.Empty;
        ProgressVisualizationMode = "None";
        Volatile.Write(ref _threadProgress, Array.Empty<DownloadThreadProgress>());
    }

    public Dictionary<string, string>? CustomHeaders { get; set; }

    public string Id = string.Empty;

    public bool IsYoutube { get; set; }

    public string? FormatId { get; set; }

    public double Progress => IsMergeProgressActive
        ? MergeProgress
        : TotalBytes > 0
            ? (double)DownloadedBytes / TotalBytes * 100
            : 0;

    public bool IsActive => Status is DownloadStatus.Downloading or DownloadStatus.Connecting or DownloadStatus.Merging;

    private string _url = string.Empty;
    public string Url
    {
        get => _url;
        set { _url = value; OnPropertyChanged(); }
    }

    public string ResolvedUrl { get; set; } = string.Empty;

    private string _fileName = string.Empty;
    public string FileName
    {
        get => _fileName;
        set { _fileName = value; OnPropertyChanged(); }
    }

    private string _savePath = string.Empty;
    public string SavePath
    {
        get => _savePath;
        set { _savePath = value; OnPropertyChanged(); }
    }

    private long _totalBytes = 0;
    public long TotalBytes
    {
        get => _totalBytes;
        set { _totalBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(Progress)); OnPropertyChanged(nameof(TotalFormatted)); }
    }

    private long _downloadedBytes = 0;
    public long DownloadedBytes
    {
        get => _downloadedBytes;
        set { _downloadedBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(Progress)); OnPropertyChanged(nameof(DownloadedFormatted)); }
    }

    private double _mergeProgress;
    public double MergeProgress
    {
        get => _mergeProgress;
        set
        {
            _mergeProgress = Math.Clamp(value, 0, 100);
            OnPropertyChanged();
            OnPropertyChanged(nameof(Progress));
        }
    }

    private bool _isMergeProgressActive;
    public bool IsMergeProgressActive
    {
        get => _isMergeProgressActive;
        set
        {
            if (_isMergeProgressActive == value)
            {
                return;
            }

            _isMergeProgressActive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Progress));
        }
    }

    private double _speedBps = 0;
    public double SpeedBps
    {
        get => _speedBps;
        set { _speedBps = value; OnPropertyChanged(); OnPropertyChanged(nameof(SpeedFormatted)); OnPropertyChanged(nameof(EtaFormatted)); }
    }

    private DownloadStatus _status = DownloadStatus.Queued;
    public DownloadStatus Status
    {
        get => _status;
        set
        {
            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(Progress));
        }
    }

    private string _errorMessage = string.Empty;
    public string ErrorMessage
    {
        get => _errorMessage;
        set { _errorMessage = value; OnPropertyChanged(); }
    }

    private int _threads = 8;
    public int Threads
    {
        get => _threads;
        set { _threads = value; OnPropertyChanged(); }
    }

    private DateTime _startTime;
    public DateTime StartTime
    {
        get => _startTime;
        set { _startTime = value; OnPropertyChanged(); }
    }

    private DateTime _endTime;
    public DateTime EndTime
    {
        get => _endTime;
        set { _endTime = value; OnPropertyChanged(); }
    }

    public string TotalFormatted => FormatBytes(TotalBytes);

    public string DownloadedFormatted => FormatBytes(DownloadedBytes);

    public string SpeedFormatted => SpeedBps > 0 ? $"{FormatBytes((long)SpeedBps)}/s" : "–";

    public string EtaFormatted
    {
        get
        {
            if (SpeedBps <= 0 || TotalBytes <= 0)
            {
                return "–";
            }

            long remaining = TotalBytes - DownloadedBytes;
            var eta = TimeSpan.FromSeconds(remaining / SpeedBps);
            return eta.TotalHours >= 1
                ? $"{(int)eta.TotalHours}g {eta.Minutes:D2}p"
                : $"{eta.Minutes:D2}:{eta.Seconds:D2}";
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:F1} KB";
        }

        if (bytes < 1024L * 1024 * 1024)
        {
            return $"{bytes / (1024.0 * 1024):F1} MB";
        }

        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
