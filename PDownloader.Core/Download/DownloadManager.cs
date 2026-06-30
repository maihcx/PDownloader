using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PDownloader.Core.Download
{
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

        private readonly ConcurrentDictionary<string, Task> _runningTaskByItem = new();

        public event Action<DownloadItem>? OnItemChanged;

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
            var task = StartAsync(item);
            _runningTaskByItem[item.Id] = task;
            return item;
        }

        public List<DownloadItem> GetAll()
        {
            lock (_lock) { return _downloads.ToList(); }
        }

        public async Task StartAsync(DownloadItem item)
        {
            if (item.Status is DownloadStatus.Downloading or DownloadStatus.Merging or DownloadStatus.Connecting)
                return;

            if (item.Status == DownloadStatus.Cancelled)
                return;

            await _sem.WaitAsync();

            item.Status = DownloadStatus.Connecting;
            OnItemChanged?.Invoke(item);

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
                    if (item.Status != DownloadStatus.Paused && item.Status != DownloadStatus.Cancelled)
                        item.Status = DownloadStatus.Paused;
                }
                catch (System.Exception ex)
                {
                    item.Status       = DownloadStatus.Error;
                    item.ErrorMessage = ex.Message;
                }

                if (item.Status != DownloadStatus.Cancelled)
                    OnItemChanged?.Invoke(item);
            }
            finally
            {
                _sem.Release();
                _ctsByItem.TryRemove(item.Id, out _);
                _runningTaskByItem.TryRemove(item.Id, out _);
            }
        }

        public void Pause(string id)
        {
            var item = Find(id); if (item == null) return;

            if (_ctsByItem.TryGetValue(id, out var cts)) cts.Cancel();
        }

        public void Resume(string id)
        {
            var item = Find(id); if (item == null) return;

            lock (_lock)
            {
                if (item.Status != DownloadStatus.Paused) return;
                item.Status = DownloadStatus.Queued;
            }

            var task = StartAsync(item);
            _runningTaskByItem[item.Id] = task;
        }

        public async Task CancelAsync(string id)
        {
            DownloadItem? item;
            Task? runningTask;
            lock (_lock)
            {
                item = _downloads.FirstOrDefault(d => d.Id == id);
                if (item == null) return;

                item.Status = DownloadStatus.Cancelled;
                _downloads.Remove(item);
            }

            _runningTaskByItem.TryGetValue(id, out runningTask);

            if (_ctsByItem.TryGetValue(id, out var cts)) cts.Cancel();

            OnItemChanged?.Invoke(item);

            if (runningTask != null)
            {
                await runningTask;
            }

            DownloadEngine.DeleteTempFiles(id, item.SavePath, item.FileName);

            _runningTaskByItem.TryRemove(id, out _);
        }

        public void Cancel(string id)
        {
            _ = CancelAsync(id);
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

        public string SerializeHistory()
            => JsonSerializer.Serialize(GetAll().Select(DownloadItemSnapshot.From), new JsonSerializerOptions
            {
                WriteIndented = true
            });

        public static List<DownloadItemSnapshot> DeserializeHistory(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<DownloadItemSnapshot>();

            try
            {
                var result = JsonSerializer.Deserialize<List<DownloadItemSnapshot>>(json);
                return result ?? new List<DownloadItemSnapshot>();
            }
            catch (JsonException)
            {
                return new List<DownloadItemSnapshot>();
            }
        }

        public DownloadItem RestoreItem(DownloadItemSnapshot snapshot)
        {
            var item = snapshot.ToDownloadItem();

            lock (_lock) { _downloads.Add(item); }
            OnItemChanged?.Invoke(item);

            bool isFinal = item.Status is DownloadStatus.Completed or DownloadStatus.Cancelled or DownloadStatus.Paused;
            if (!isFinal)
            {
                item.Status = DownloadStatus.Queued;
                var task = StartAsync(item);
                _runningTaskByItem[item.Id] = task;
            }

            return item;
        }

        public List<DownloadItem> RestoreHistory(string json)
        {
            var snapshots = DeserializeHistory(json);
            var restored = new List<DownloadItem>(snapshots.Count);
            foreach (var snap in snapshots)
            {
                restored.Add(RestoreItem(snap));
            }
            return restored;
        }

        public string SerializeList()
            => JsonSerializer.Serialize(GetAll().Select(DownloadItemDto.From), new JsonSerializerOptions
            {
                WriteIndented = true
            });

        public static string SerializeItem(DownloadItem item)
            => JsonSerializer.Serialize(DownloadItemDto.From(item), new JsonSerializerOptions
            {
                WriteIndented = true
            });

        public static List<DownloadItemDto> DeserializeList(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<DownloadItemDto>();

            try
            {
                var result = JsonSerializer.Deserialize<List<DownloadItemDto>>(json);
                return result ?? new List<DownloadItemDto>();
            }
            catch (JsonException)
            {
                return new List<DownloadItemDto>();
            }
        }

        public static DownloadItemDto? DeserializeItem(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return JsonSerializer.Deserialize<DownloadItemDto>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    public record DownloadItemSnapshot(
        string Id, string Url, string FileName, string SavePath,
        int Threads, bool IsYoutube, string? FormatId,
        long TotalBytes, long DownloadedBytes,
        string Status, string ErrorMessage,
        DateTime StartTime, DateTime EndTime)
    {
        public static DownloadItemSnapshot From(DownloadItem i) => new(
            i.Id, i.Url, i.FileName, i.SavePath,
            i.Threads, i.IsYoutube, i.FormatId,
            i.TotalBytes, i.DownloadedBytes,
            i.Status.ToString(), i.ErrorMessage,
            i.StartTime, i.EndTime);

        public DownloadItem ToDownloadItem()
        {
            var status = Enum.TryParse<DownloadStatus>(Status, out var s) ? s : DownloadStatus.Queued;
            return new DownloadItem
            {
                Id              = Id,
                Url             = Url,
                FileName        = FileName,
                SavePath        = SavePath,
                Threads         = Threads,
                IsYoutube       = IsYoutube,
                FormatId        = FormatId,
                TotalBytes      = TotalBytes,
                DownloadedBytes = DownloadedBytes,
                Status          = status,
                ErrorMessage    = ErrorMessage,
                StartTime       = StartTime,
                EndTime         = EndTime
            };
        }
    }

    public record DownloadItemDto(
        string Id, string Url, string FileName, string SavePath,
        long TotalBytes, long DownloadedBytes, double SpeedBps,
        double Progress, string Status,
        string SpeedFormatted, string EtaFormatted,
        string TotalFormatted, string DownloadedFormatted,
        string ErrorMessage, bool IsActive)
    {
        public static DownloadItemDto From(DownloadItem i) => new(
            i.Id.ToString(), i.Url, i.FileName, i.SavePath,
            i.TotalBytes, i.DownloadedBytes, i.SpeedBps,
            i.Progress, i.Status.ToString(),
            i.SpeedFormatted, i.EtaFormatted,
            i.TotalFormatted, i.DownloadedFormatted,
            i.ErrorMessage, i.IsActive);
    }
}