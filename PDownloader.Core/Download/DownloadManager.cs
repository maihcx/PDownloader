using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PDownloader.Core.Download;

/// <summary>
/// Manages all download tasks in Core (no WPF dependency).
/// Broadcasts state changes to Runner and PDownloader.exe via CFS.
/// </summary>
public class DownloadManager
{
    public static readonly DownloadManager Instance = new();

    private readonly List<DownloadItem> _downloads = new();
    private readonly object _lock = new();
    private const int MaxConcurrent = 3;
    private readonly SemaphoreSlim _sem = new(MaxConcurrent, MaxConcurrent);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _ctsByItem = new();

    // Called by Core when status changes — broadcasts to all listeners
    public event Action<DownloadItem>? OnItemChanged;

    // ── Add to queue ──────────────────────────────────────────────────────────
    public DownloadItem Enqueue(string id, string url, string saveTo = "", string fileName = "", int threads = 8, bool isYoutube = false, string? formatId = null)
    {
        var item = new DownloadItem
        {
            Id       = id,
            Url      = url,
            SavePath = saveTo,
            FileName = fileName,
            Threads  = threads,
            Status   = DownloadStatus.Queued,
            IsYoutube = isYoutube,
            FormatId = formatId
        };

        lock (_lock) { _downloads.Add(item); }

        OnItemChanged?.Invoke(item);
        _ = StartAsync(item);
        return item;
    }

    // ── Get all (for list queries) ────────────────────────────────────────────
    public List<DownloadItem> GetAll()
    {
        lock (_lock) { return _downloads.ToList(); }
    }

    // ── Start ─────────────────────────────────────────────────────────────────
    public async Task StartAsync(DownloadItem item)
    {
        if (item.Status is DownloadStatus.Downloading or DownloadStatus.Merging) return;
        item.Status = DownloadStatus.Connecting;
        OnItemChanged?.Invoke(item);

        await _sem.WaitAsync();
        try
        {
            var cts = new CancellationTokenSource();
            _ctsByItem[item.Id] = cts;

            var progress = new Progress<DownloadProgress>(p =>
            {
                item.DownloadedBytes = p.DownloadedBytes;
                item.SpeedBps        = p.SpeedBps;
                OnItemChanged?.Invoke(item);
            });

            var engine = new DownloadEngine(item, progress, cts.Token);
            try
            {
                await engine.RunAsync();
            }
            catch (OperationCanceledException)
            {
                if (item.Status != DownloadStatus.Paused)
                    item.Status = DownloadStatus.Paused;
            }
            catch (System.Exception ex)
            {
                item.Status       = DownloadStatus.Error;
                item.ErrorMessage = ex.Message;
            }
            OnItemChanged?.Invoke(item);
        }
        finally
        {
            _sem.Release();
            _ctsByItem.TryRemove(item.Id, out _);
        }
    }

    public void Pause(string id)
    {
        var item = Find(id); if (item == null) return;
        item.Status = DownloadStatus.Paused;
        if (_ctsByItem.TryGetValue(id, out var cts)) cts.Cancel();
        OnItemChanged?.Invoke(item);
    }

    public void Resume(string id)
    {
        var item = Find(id); if (item == null) return;
        if (item.Status == DownloadStatus.Paused) _ = StartAsync(item);
    }

    public void Cancel(string id)
    {
        var item = Find(id); if (item == null) return;
        if (_ctsByItem.TryGetValue(id, out var cts)) cts.Cancel();
        lock (_lock) { _downloads.Remove(item); }
        OnItemChanged?.Invoke(item);
    }

    public void Retry(string id)
    {
        var item = Find(id); if (item == null) return;
        item.Status         = DownloadStatus.Queued;
        item.ErrorMessage   = string.Empty;
        item.DownloadedBytes = 0;
        _ = StartAsync(item);
    }

    public DownloadItem? Find(string id)
    {
        lock (_lock) { return _downloads.FirstOrDefault(d => d.Id == id); }
    }

    // ── Serialize for CFS transmission ───────────────────────────────────────
    public string SerializeList()
        => JsonSerializer.Serialize(GetAll().Select(DownloadItemDto.From));

    public static string SerializeItem(DownloadItem item)
        => JsonSerializer.Serialize(DownloadItemDto.From(item));
}

// ── Lightweight DTO for CFS (no INotifyPropertyChanged overhead) ─────────────
public record DownloadItemDto(
    string Id, string Url, string FileName, string SavePath,
    long TotalBytes, long DownloadedBytes, double SpeedBps,
    double Progress, string Status, string StatusText,
    string SpeedFormatted, string EtaFormatted,
    string TotalFormatted, string DownloadedFormatted,
    string ErrorMessage, bool IsActive)
{
    public static DownloadItemDto From(DownloadItem i) => new(
        i.Id.ToString(), i.Url, i.FileName, i.SavePath,
        i.TotalBytes, i.DownloadedBytes, i.SpeedBps,
        i.Progress, i.Status.ToString(), i.StatusText,
        i.SpeedFormatted, i.EtaFormatted,
        i.TotalFormatted, i.DownloadedFormatted,
        i.ErrorMessage, i.IsActive);
}
