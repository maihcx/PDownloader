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

namespace PDownloader.Downloads.Models;

public class DownloadItem : INotifyPropertyChanged
{
    private DownloadThreadProgress[] _threadProgress = Array.Empty<DownloadThreadProgress>();
    private TorrentFileProgressDto[] _torrentFiles = Array.Empty<TorrentFileProgressDto>();

    public DownloadProgressVisualizationMode ProgressVisualizationMode { get; private set; } = DownloadProgressVisualizationMode.None;

    public string ProgressVisualizationStage { get; private set; } = string.Empty;

    public IReadOnlyList<DownloadThreadProgress> GetThreadProgressSnapshot() =>
        Volatile.Read(ref _threadProgress);

    public void SetThreadProgress(
        string stage,
        IReadOnlyCollection<DownloadThreadProgress> progress)
    {
        ProgressVisualizationStage = stage;
        ProgressVisualizationMode = DownloadProgressVisualizationMode.Threads;
        Volatile.Write(ref _threadProgress, progress.ToArray());
    }

    public void SetProgressVisualizationUnsupported(string stage)
    {
        ProgressVisualizationStage = stage;
        ProgressVisualizationMode = DownloadProgressVisualizationMode.Unsupported;
        Volatile.Write(ref _threadProgress, Array.Empty<DownloadThreadProgress>());
    }

    public void ClearProgressVisualization()
    {
        ProgressVisualizationStage = string.Empty;
        ProgressVisualizationMode = DownloadProgressVisualizationMode.None;
        Volatile.Write(ref _threadProgress, Array.Empty<DownloadThreadProgress>());
    }

    public IReadOnlyList<TorrentFileProgressDto> GetTorrentFilesSnapshot() =>
        Volatile.Read(ref _torrentFiles).Select(CloneTorrentFile).ToArray();

    public void SetTorrentFiles(IEnumerable<TorrentFileProgressDto> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        Volatile.Write(ref _torrentFiles, files.Select(CloneTorrentFile).ToArray());
        OnPropertyChanged(nameof(GetTorrentFilesSnapshot));
    }

    public void UpdateTorrentFile(
        int index,
        long downloadedBytes,
        double speedBps,
        DownloadStatus status,
        string errorMessage = "",
        string? savePath = null)
    {
        TorrentFileProgressDto[] current = Volatile.Read(ref _torrentFiles);
        TorrentFileProgressDto[] next = current.Select(CloneTorrentFile).ToArray();
        TorrentFileProgressDto? file = next.FirstOrDefault(candidate => candidate.Index == index);
        if (file is null)
        {
            return;
        }

        file.DownloadedBytes = Math.Clamp(downloadedBytes, 0, Math.Max(0, file.Length));
        file.SpeedBps = Math.Max(0, speedBps);
        file.Progress = file.Length > 0
            ? Math.Clamp((double)file.DownloadedBytes / file.Length * 100, 0, 100)
            : status == DownloadStatus.Completed ? 100 : 0;
        file.Status = status;
        file.ErrorMessage = errorMessage;
        if (!string.IsNullOrWhiteSpace(savePath))
        {
            file.SavePath = savePath;
        }

        Volatile.Write(ref _torrentFiles, next);
        OnPropertyChanged(nameof(GetTorrentFilesSnapshot));
    }

    public void SetTorrentFileStatus(DownloadStatus status, string errorMessage = "")
    {
        TorrentFileProgressDto[] next = Volatile.Read(ref _torrentFiles)
            .Select(CloneTorrentFile)
            .ToArray();
        foreach (TorrentFileProgressDto file in next.Where(file => file.Status != DownloadStatus.Completed))
        {
            file.Status = status;
            file.ErrorMessage = errorMessage;
            file.SpeedBps = 0;
        }

        Volatile.Write(ref _torrentFiles, next);
        OnPropertyChanged(nameof(GetTorrentFilesSnapshot));
    }

    private static TorrentFileProgressDto CloneTorrentFile(TorrentFileProgressDto file) => new()
    {
        Index = file.Index,
        RelativePath = file.RelativePath,
        FileName = file.FileName,
        SavePath = file.SavePath,
        Length = file.Length,
        DownloadedBytes = file.DownloadedBytes,
        SpeedBps = file.SpeedBps,
        Progress = file.Progress,
        Status = file.Status,
        ErrorMessage = file.ErrorMessage
    };

    public Dictionary<string, string>? CustomHeaders { get; set; }

    public string Id = string.Empty;

    public bool IsYoutube { get; set; }

    public string? FormatId { get; set; }

    public DownloadKind DownloadKind { get; set; } = DownloadKind.Http;

    public string DestinationFolder { get; set; } = string.Empty;

    public string TorrentInfoHash { get; set; } = string.Empty;

    public int TorrentFileIndex { get; set; } = -1;

    public string TorrentRelativePath { get; set; } = string.Empty;

    public string TorrentDestinationPath { get; set; } = string.Empty;

    private double _downloadProgressPercent;
    // Fallback for transfers with known work units but unknown total byte size.
    public double DownloadProgressPercent
    {
        get => _downloadProgressPercent;
        set
        {
            _downloadProgressPercent = double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Progress));
        }
    }

    public double Progress => Status == DownloadStatus.Completed
        ? 100
        : IsMergeProgressActive
            ? MergeProgress
            : IsTotalBytesEstimated && DownloadProgressPercent > 0
                ? Math.Min(99, DownloadProgressPercent)
            : TotalBytes > 0
                ? Math.Clamp((double)DownloadedBytes / TotalBytes * 100, 0, IsTotalBytesEstimated ? 99 : 100)
                : DownloadProgressPercent;

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

    public string TempRootPath { get; set; } = string.Empty;

    private FileMergeMode _mergeMode = FileMergeMode.Balanced;
    public FileMergeMode MergeMode
    {
        get => _mergeMode;
        set
        {
            if (_mergeMode == value)
            {
                return;
            }

            _mergeMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanPause));
            OnPropertyChanged(nameof(CanResume));
        }
    }

    public bool CanPause => Status is DownloadStatus.Downloading
        || (Status == DownloadStatus.Merging
            && MergeMode != FileMergeMode.HighPerformance);

    public bool CanResume => Status == DownloadStatus.Paused
        && !(IsMergeProgressActive
            && MergeMode == FileMergeMode.HighPerformance);

    private long _totalBytes = 0;
    private bool _isTotalBytesEstimated;
    public bool IsTotalBytesEstimated
    {
        get => _isTotalBytesEstimated;
        set
        {
            _isTotalBytesEstimated = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TotalFormatted));
            OnPropertyChanged(nameof(Progress));
        }
    }

    public void SetTotalBytes(long bytes, bool isEstimated = false)
    {
        IsTotalBytesEstimated = bytes > 0 && isEstimated;
        TotalBytes = Math.Max(0, bytes);
    }

    public long TotalBytes
    {
        get => _totalBytes;
        set { _totalBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(Progress)); OnPropertyChanged(nameof(TotalFormatted)); OnPropertyChanged(nameof(EtaFormatted)); }
    }

    private long _downloadedBytes = 0;
    public long DownloadedBytes
    {
        get => _downloadedBytes;
        set { _downloadedBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(Progress)); OnPropertyChanged(nameof(DownloadedFormatted)); OnPropertyChanged(nameof(EtaFormatted)); }
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
            OnPropertyChanged(nameof(CanResume));
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
            OnPropertyChanged(nameof(CanPause));
            OnPropertyChanged(nameof(CanResume));
        }
    }

    private string _errorMessage = string.Empty;
    public string ErrorMessage
    {
        get => _errorMessage;
        set { _errorMessage = value; OnPropertyChanged(); }
    }

    private string _md5Hash = string.Empty;
    public string Md5Hash
    {
        get => _md5Hash;
        set { _md5Hash = value; OnPropertyChanged(); }
    }

    private string _sha1Hash = string.Empty;
    public string Sha1Hash
    {
        get => _sha1Hash;
        set { _sha1Hash = value; OnPropertyChanged(); }
    }

    private string _sha256Hash = string.Empty;
    public string Sha256Hash
    {
        get => _sha256Hash;
        set { _sha256Hash = value; OnPropertyChanged(); }
    }

    public bool HasFileHashes =>
        !string.IsNullOrWhiteSpace(Md5Hash)
        && !string.IsNullOrWhiteSpace(Sha1Hash)
        && !string.IsNullOrWhiteSpace(Sha256Hash);

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

    public string TotalFormatted => TotalBytes > 0
        ? (IsTotalBytesEstimated ? "≈ " : string.Empty) + FormatBytes(TotalBytes) : "–";

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

            long remaining = Math.Max(0, TotalBytes - DownloadedBytes);
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
