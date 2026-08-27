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

namespace PDownloader.Downloads;

public class DownloadManager : IDisposable
{
    public static readonly DownloadManager Instance = new();

    private readonly List<DownloadItem> _downloads = new();
    private readonly object _lock = new();
    private const int MaxConcurrent = 3;
    private readonly SemaphoreSlim _sem = new(MaxConcurrent, MaxConcurrent);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _ctsByItem = new();
    private bool _disposed;

    private readonly ConcurrentDictionary<string, Task> _runningTaskByItem = new();
    private readonly ConcurrentDictionary<string, Task> _hashTaskByItem = new();
    private readonly SemaphoreSlim _hashSemaphore = new(1, 1);

    public event Action<DownloadItem>? OnItemChanged;

    public DownloadItem Enqueue(
        string id,
        string url,
        string saveTo = "",
        string fileName = "",
        int threads = 8,
        bool isYoutube = false,
        string? formatId = null,
        Dictionary<string, string>? customHeaders = null,
        FileMergeMode mergeMode = FileMergeMode.Balanced)
    {
        var item = new DownloadItem
        {
            Id = id,
            Url = url,
            SavePath = saveTo,
            FileName = fileName,
            Threads = threads,
            Status = DownloadStatus.Queued,
            IsYoutube = isYoutube,
            FormatId = formatId,
            CustomHeaders = customHeaders,
            MergeMode = mergeMode
        };

        _ = new DownloadPathService().GetTempDirectory(item);

        lock (_lock) { _downloads.Add(item); }

        OnItemChanged?.Invoke(item);
        Task task = StartAsync(item);
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
        {
            return;
        }

        if (item.Status == DownloadStatus.Cancelled)
        {
            return;
        }

        await _sem.WaitAsync();

        item.Status = DownloadStatus.Connecting;
        OnItemChanged?.Invoke(item);

        try
        {
            var cts = new CancellationTokenSource();
            _ctsByItem[item.Id] = cts;

            var progress = new Progress<DownloadProgress>(_ =>
            {
                OnItemChanged?.Invoke(item);
            });

            const int maxAutoRetries = 5;
            int attempt = 0;

            while (true)
            {
                var engine = new DownloadEngine(item, progress, cts.Token);
                try
                {
                    await engine.RunAsync();
                    break;
                }
                catch (OperationCanceledException)
                {
                    if (item.Status != DownloadStatus.Paused && item.Status != DownloadStatus.Cancelled)
                    {
                        item.Status = DownloadStatus.Paused;
                    }

                    break;
                }
                catch (System.Exception ex)
                {
                    if (attempt >= maxAutoRetries)
                    {
                        item.Status = DownloadStatus.Error;
                        item.ErrorMessage = ex.Message;
                        break;
                    }

                    attempt++;
                    item.Status = DownloadStatus.Retrying;
                    item.ErrorMessage = $"An error occurred! Retrying ({attempt}/{maxAutoRetries})... Please wait...";
                    OnItemChanged?.Invoke(item);

                    int delayMilliseconds = 2000;
                    try
                    {
                        await Task.Delay(delayMilliseconds, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        if (item.Status != DownloadStatus.Paused && item.Status != DownloadStatus.Cancelled)
                        {
                            item.Status = DownloadStatus.Paused;
                        }

                        break;
                    }
                }
            }

            if (item.Status != DownloadStatus.Cancelled)
            {
                OnItemChanged?.Invoke(item);

                if (item.Status == DownloadStatus.Completed)
                {
                    QueueHashCalculation(item);
                }
            }
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
        DownloadItem? item = Find(id);
        Pause(item);
    }

    public void Pause(DownloadItem? item)
    {
        if (item == null)
        {
            return;
        }

        if (!item.CanPause)
        {
            return;
        }

        if (_ctsByItem.TryGetValue(item.Id, out CancellationTokenSource? cts))
        {
            cts.Cancel();
        }
    }

    public void Resume(string id, bool isShowRunner = true)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        DownloadItem? item = Find(id);

        Resume(item);
    }

    public void Resume(DownloadItem? item, bool isShowRunner = true)
    {
        if (item == null)
        {
            return;
        }

        if (isShowRunner)
        {
            DownloadRuntime.RequestRunner(item.Id, new()
            {
                id = item.Id,
                fileName = item.FileName,
                formatId = item.FormatId ?? string.Empty,
                filesize = item.TotalBytes,
                saveTo = item.SavePath,
                url = item.Url,
                downloadRunner = "runner",
                threads = item.Threads
            });
        }

        lock (_lock)
        {
            if (!item.CanResume)
            {
                return;
            }

            item.Status = DownloadStatus.Queued;
        }

        Task task = StartAsync(item);
        _runningTaskByItem[item.Id] = task;
    }

    public void PauseAll()
    {
        foreach (DownloadItem item in _downloads)
        {
            if (item.Status != DownloadStatus.Completed && item.Status != DownloadStatus.Error)
            {
                Pause(item);
            }
        }
    }

    public void ResumeAll()
    {
        foreach (DownloadItem item in _downloads)
        {
            if (item.Status == DownloadStatus.Paused)
            {
                Resume(item);
            }
        }
    }

    public void RetryAll()
    {
        foreach (DownloadItem item in _downloads)
        {
            if (item.Status == DownloadStatus.Error)
            {
                Retry(item);
            }
        }
    }

    public void ClearAll(string state)
    {
        ClearAllAsync(state).Wait();
    }

    public async Task ClearAllAsync(string state)
    {
        if (state.Equals("completed"))
        {
            for (int i = _downloads.Count - 1; i >= 0; i--)
            {
                DownloadItem item = _downloads[i];
                if (item.Status == DownloadStatus.Completed)
                {
                    await CancelAsync(item);
                }
            }
        }
        else if (state.Equals("all"))
        {
            for (int i = _downloads.Count - 1; i >= 0; i--)
            {
                DownloadItem item = _downloads[i];
                await CancelAsync(item);
            }
        }
    }

    public async Task CancelAsync(string id)
    {
        DownloadItem? item = _downloads.FirstOrDefault(d => d.Id == id);

        if (item != null)
        {
            await CancelAsync(item);
        }
    }

    public async Task CancelAsync(DownloadItem item)
    {
        Task? runningTask;
        lock (_lock)
        {
            if (item == null)
            {
                return;
            }

            item.Status = DownloadStatus.Cancelled;
            _downloads.Remove(item);
        }

        _runningTaskByItem.TryGetValue(item.Id, out runningTask);

        if (_ctsByItem.TryGetValue(item.Id, out CancellationTokenSource? cts))
        {
            cts.Cancel();
        }

        OnItemChanged?.Invoke(item);

        if (runningTask != null)
        {
            await runningTask;
        }

        DownloadEngine.DeleteTempFiles(item);

        _runningTaskByItem.TryRemove(item.Id, out _);
    }

    public void Cancel(string id)
    {
        CancelAsync(id).Wait();
    }

    public void Retry(string id)
    {
        DownloadItem? item = Find(id);
        Retry(item);
    }

    public void Retry(DownloadItem? item)
    {
        if (item == null)
        {
            return;
        }

        bool hasPendingMerge = DownloadEngine.HasPendingMerge(item);

        item.Status = DownloadStatus.Queued;
        item.ErrorMessage = string.Empty;
        item.SpeedBps = 0;
        item.MergeProgress = 0;
        item.IsMergeProgressActive = hasPendingMerge;

        if (!hasPendingMerge)
        {
            item.DownloadedBytes = 0;
        }

        Task task = StartAsync(item);
        _runningTaskByItem[item.Id] = task;
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
        {
            return new List<DownloadItemSnapshot>();
        }

        try
        {
            List<DownloadItemSnapshot>? result = JsonSerializer.Deserialize<List<DownloadItemSnapshot>>(json);
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

        if (item.Status is DownloadStatus.Queued
            or DownloadStatus.Connecting
            or DownloadStatus.Downloading
            or DownloadStatus.Merging)
        {
            item.Status = DownloadStatus.Paused;
            item.SpeedBps = 0;
        }

        if ((item.Status is DownloadStatus.Paused or DownloadStatus.Error)
            && DownloadEngine.TryGetPendingMergeProgress(
                item,
                out double pendingMergeProgress))
        {
            item.MergeProgress = pendingMergeProgress;
            item.IsMergeProgressActive = true;
        }

        lock (_lock) { _downloads.Add(item); }

        OnItemChanged?.Invoke(item);

        if (item.Status == DownloadStatus.Completed)
        {
            QueueHashCalculation(item);
        }

        if (item.Status == DownloadStatus.Retrying)
        {
            Retry(item);
        }

        return item;
    }

    private void QueueHashCalculation(DownloadItem item)
    {
        if (item.Status != DownloadStatus.Completed
            || item.HasFileHashes
            || string.IsNullOrWhiteSpace(item.SavePath)
            || !File.Exists(item.SavePath))
        {
            return;
        }

        _hashTaskByItem.GetOrAdd(
            item.Id,
            _ => Task.Run(() => CalculateFileHashesAsync(item)));
    }

    private async Task CalculateFileHashesAsync(DownloadItem item)
    {
        string filePath = item.SavePath;

        try
        {
            await _hashSemaphore.WaitAsync();

            FileHashResult hashes;
            try
            {
                hashes = await FileHashCalculator.ComputeAsync(filePath);
            }
            finally
            {
                _hashSemaphore.Release();
            }

            if (item.Status != DownloadStatus.Completed
                || !string.Equals(item.SavePath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            item.Md5Hash = hashes.Md5;
            item.Sha1Hash = hashes.Sha1;
            item.Sha256Hash = hashes.Sha256;
            OnItemChanged?.Invoke(item);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[DownloadManager] Cannot calculate hash for '{filePath}': {ex.Message}");
        }
        finally
        {
            _hashTaskByItem.TryRemove(item.Id, out _);
        }
    }

    public List<DownloadItem> RestoreHistory(string json)
    {
        List<DownloadItemSnapshot> snapshots = DeserializeHistory(json);
        var restored = new List<DownloadItem>(snapshots.Count);
        foreach (DownloadItemSnapshot snap in snapshots)
        {
            restored.Add(RestoreItem(snap));
        }

        return restored;
    }

    public string SerializeList()
        => JsonSerializer.Serialize(GetAll().Select(DownloadItemContractMapper.From), new JsonSerializerOptions
        {
            WriteIndented = true
        });

    public static string SerializeItem(DownloadItem item)
        => JsonSerializer.Serialize(DownloadItemContractMapper.From(item), new JsonSerializerOptions
        {
            WriteIndented = true
        });

    public static List<DownloadItemDto> DeserializeList(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<DownloadItemDto>();
        }

        try
        {
            List<DownloadItemDto>? result = JsonSerializer.Deserialize<List<DownloadItemDto>>(json);
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
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DownloadItemDto>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _sem.Dispose();
            _hashSemaphore.Dispose();
        }

        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
    }
}
