using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PDownloader.Runner.Models;

public enum DownloadStatus
{
    Queued,
    Connecting,
    Downloading,
    Paused,
    Merging,
    Completed,
    Error
}

public class DownloadItem : INotifyPropertyChanged
{
    private string _url        = string.Empty;
    private string _fileName   = string.Empty;
    private string _savePath   = string.Empty;
    private long   _totalBytes = 0;
    private long   _downloadedBytes = 0;
    private double _speedBps   = 0;
    private DownloadStatus _status = DownloadStatus.Queued;
    private string _errorMessage   = string.Empty;
    private int    _threads    = 8;
    private DateTime _startTime;
    private DateTime _endTime;

    public Guid Id { get; } = Guid.NewGuid();

    public string Url
    {
        get => _url;
        set { _url = value; OnPropertyChanged(); }
    }

    public string FileName
    {
        get => _fileName;
        set { _fileName = value; OnPropertyChanged(); }
    }

    public string SavePath
    {
        get => _savePath;
        set { _savePath = value; OnPropertyChanged(); }
    }

    public long TotalBytes
    {
        get => _totalBytes;
        set { _totalBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(Progress)); OnPropertyChanged(nameof(TotalFormatted)); }
    }

    public long DownloadedBytes
    {
        get => _downloadedBytes;
        set { _downloadedBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(Progress)); OnPropertyChanged(nameof(DownloadedFormatted)); }
    }

    public double Progress => TotalBytes > 0 ? (double)DownloadedBytes / TotalBytes * 100 : 0;

    public double SpeedBps
    {
        get => _speedBps;
        set { _speedBps = value; OnPropertyChanged(); OnPropertyChanged(nameof(SpeedFormatted)); OnPropertyChanged(nameof(EtaFormatted)); }
    }

    public DownloadStatus Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(IsActive)); }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set { _errorMessage = value; OnPropertyChanged(); }
    }

    public int Threads
    {
        get => _threads;
        set { _threads = value; OnPropertyChanged(); }
    }

    public DateTime StartTime
    {
        get => _startTime;
        set { _startTime = value; OnPropertyChanged(); }
    }

    public DateTime EndTime
    {
        get => _endTime;
        set { _endTime = value; OnPropertyChanged(); }
    }

    // ── Computed display properties ────────────────────────────────────────────
    public bool IsActive => Status is DownloadStatus.Downloading or DownloadStatus.Connecting or DownloadStatus.Merging;

    public string StatusText => Status switch
    {
        DownloadStatus.Queued      => "Đang chờ",
        DownloadStatus.Connecting  => "Đang kết nối...",
        DownloadStatus.Downloading => "Đang tải",
        DownloadStatus.Paused      => "Tạm dừng",
        DownloadStatus.Merging     => "Đang ghép file...",
        DownloadStatus.Completed   => "Hoàn thành",
        DownloadStatus.Error       => $"Lỗi: {ErrorMessage}",
        _                          => string.Empty
    };

    public string TotalFormatted   => FormatBytes(TotalBytes);
    public string DownloadedFormatted => FormatBytes(DownloadedBytes);
    public string SpeedFormatted   => SpeedBps > 0 ? $"{FormatBytes((long)SpeedBps)}/s" : "–";

    public string EtaFormatted
    {
        get
        {
            if (SpeedBps <= 0 || TotalBytes <= 0) return "–";
            long remaining = TotalBytes - DownloadedBytes;
            var eta = TimeSpan.FromSeconds(remaining / SpeedBps);
            return eta.TotalHours >= 1
                ? $"{(int)eta.TotalHours}g {eta.Minutes:D2}p"
                : $"{eta.Minutes:D2}:{eta.Seconds:D2}";
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)  return "0 B";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
