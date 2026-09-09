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

using MonoTorrent;
using MonoTorrent.Client;
using System.Net;

namespace PDownloader.Downloads.Torrents;

/// <summary>
/// Owns the single MonoTorrent engine used by Core. Multiple Runner download
/// items for the same info-hash attach to one shared TorrentManager so pieces
/// crossing file boundaries are never fetched by duplicate swarms.
/// </summary>
public sealed class TorrentEngineService : IAsyncDisposable
{
    private const int MaximumMetadataBytes = 16 * 1024 * 1024;
    private static readonly TimeSpan MetadataDownloadTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ExactSourceAttemptTimeout = TimeSpan.FromSeconds(8);
    private const int ExactSourceAttemptCount = 2;
    private static readonly TimeSpan StopTimeout = TimeSpan.FromMilliseconds(100);

    private readonly string _cacheRoot;
    private readonly string _stagingRoot;
    // Runtime handoff for files belonging to a download that has already been
    // created. PrepareAsync deliberately never reads this collection: every
    // user-requested analysis downloads and parses fresh metadata.
    private readonly ConcurrentDictionary<string, TorrentPreparation> _registeredPreparations =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PreparationLockEntry> _preparationLocks =
        new(StringComparer.Ordinal);
    private readonly object _preparationLocksSync = new();
    private readonly Dictionary<string, TorrentBatch> _batches =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _reservedDestinationPaths =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private ClientEngine? _engine;
    private TaskCompletionSource? _downloadsDrained;
    private TaskCompletionSource? _preparationsDrained;
    private int _metadataEngineUsers;
    private int _activeDownloads;
    private int _activePreparations;
    private int _disposed;

    public TorrentEngineService()
    {
        _cacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SM SOFT", "PDownloader", "TorrentCache");
        _stagingRoot = Path.Combine(_cacheRoot, "staging");
    }

    public async Task<TorrentPreparation> PrepareAsync(
        string source,
        Dictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _shutdown.Token);
        CancellationToken operationToken = operationCancellation.Token;
        await BeginPreparationAsync(operationToken).ConfigureAwait(false);
        PreparationLockEntry? preparationLock = null;
        bool lockEntered = false;
        try
        {
            string preparationKey = GetPreparationKey(source);
            preparationLock = RentPreparationLock(preparationKey);
            await preparationLock.Semaphore.WaitAsync(operationToken).ConfigureAwait(false);
            lockEntered = true;
            return await PrepareCoreAsync(source, headers, operationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            if (preparationLock is not null)
            {
                ReturnPreparationLock(preparationLock, lockEntered);
            }

            await EndPreparationAsync().ConfigureAwait(false);
        }
    }

    private static string GetPreparationKey(string source)
    {
        string normalizedSource = source.Trim();
        if (DownloadSource.IsMagnet(normalizedSource)
            && MagnetLink.TryParse(normalizedSource, out MagnetLink? magnet))
        {
            return $"magnet:{magnet.InfoHashes.V1OrV2.ToHex()}";
        }

        return $"source:{normalizedSource}";
    }

    private PreparationLockEntry RentPreparationLock(string key)
    {
        lock (_preparationLocksSync)
        {
            if (!_preparationLocks.TryGetValue(key, out PreparationLockEntry? entry))
            {
                entry = new PreparationLockEntry(key);
                _preparationLocks.Add(key, entry);
            }

            entry.Users++;
            return entry;
        }
    }

    private void ReturnPreparationLock(PreparationLockEntry entry, bool lockEntered)
    {
        if (lockEntered)
        {
            entry.Semaphore.Release();
        }

        lock (_preparationLocksSync)
        {
            entry.Users--;
            if (entry.Users != 0)
            {
                return;
            }

            _preparationLocks.Remove(entry.Key);
            entry.Semaphore.Dispose();
        }
    }

    private async Task<TorrentPreparation> PrepareCoreAsync(
        string source,
        Dictionary<string, string>? headers,
        CancellationToken cancellationToken)
    {
        source = source.Trim();
        byte[] metadata;
        if (DownloadSource.IsMagnet(source))
        {
            if (!MagnetLink.TryParse(source, out MagnetLink? magnet))
            {
                throw new InvalidDataException("The magnet link is invalid.");
            }

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeoutSource.CancelAfter(MetadataDownloadTimeout);

            try
            {
                metadata = await TryDownloadExactSourceAsync(
                    source,
                    magnet,
                    timeoutSource.Token).ConfigureAwait(false)
                    ?? await DownloadMagnetMetadataAsync(
                        magnet,
                        timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                timeoutSource.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Torrent metadata could not be loaded within "
                    + $"{MetadataDownloadTimeout.TotalSeconds:0} seconds. "
                    + "No metadata peer responded. Check the magnet trackers "
                    + "or try a direct .torrent URL.");
            }
        }
        else
        {
            metadata = await DownloadTorrentFileAsync(source, headers, cancellationToken)
                .ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        Torrent torrent = Torrent.Load(metadata);
        if (torrent.Files.Count == 0)
        {
            throw new InvalidDataException("The torrent does not contain any files.");
        }

        var preparation = new TorrentPreparation(metadata, torrent);
        if (preparation.Files.Count == 0)
        {
            throw new InvalidDataException("The torrent does not contain any downloadable files.");
        }

        _registeredPreparations[preparation.InfoHash] = preparation;
        return preparation;
    }

    public void Register(TorrentPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        _registeredPreparations[preparation.InfoHash] = preparation;
    }

    public async Task<string> DownloadFileAsync(
        DownloadItem item,
        string destinationPath,
        Action<long, double> reportProgress,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(reportProgress);

        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _shutdown.Token);
        CancellationToken operationToken = operationCancellation.Token;
        await BeginDownloadAsync(operationToken).ConfigureAwait(false);

        try
        {
            TorrentPreparation preparation = await GetPreparationAsync(item, operationToken)
                .ConfigureAwait(false);
            TorrentAttachment attachment = await AttachAsync(
                preparation,
                item.TorrentFileIndex,
                destinationPath,
                operationToken).ConfigureAwait(false);
            item.TorrentDestinationPath = attachment.DestinationPath;

            try
            {
                TorrentPreparedFile preparedFile = preparation.Files.First(file =>
                    file.Index == item.TorrentFileIndex);
                if (!string.IsNullOrWhiteSpace(item.TorrentRelativePath)
                    && !string.Equals(
                        item.TorrentRelativePath,
                        preparedFile.RelativePath,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Torrent file metadata does not match the saved download.");
                }

                item.TorrentRelativePath = preparedFile.RelativePath;
                item.SetTotalBytes(preparedFile.Length);
                long lastBytes = attachment.File.BytesDownloaded();
                long lastTick = Stopwatch.GetTimestamp();
                reportProgress(lastBytes, 0);

                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
                while (attachment.File.Length > 0
                    && attachment.File.BitField.PercentComplete < 99.999)
                {
                    operationToken.ThrowIfCancellationRequested();
                    if (attachment.Batch.Manager.State == TorrentState.Error)
                    {
                        throw attachment.Batch.Manager.Error?.Exception
                            ?? new IOException("The torrent engine entered an error state.");
                    }

                    await timer.WaitForNextTickAsync(operationToken).ConfigureAwait(false);
                    long bytes = Math.Min(
                        attachment.File.Length,
                        attachment.File.BytesDownloaded());
                    long now = Stopwatch.GetTimestamp();
                    double seconds = Stopwatch.GetElapsedTime(lastTick, now).TotalSeconds;
                    double speed = seconds > 0 ? Math.Max(0, bytes - lastBytes) / seconds : 0;
                    reportProgress(bytes, speed);
                    lastBytes = bytes;
                    lastTick = now;
                }

                operationToken.ThrowIfCancellationRequested();
                reportProgress(attachment.File.Length, 0);
            }
            finally
            {
                await DetachAsync(attachment).ConfigureAwait(false);
            }

            return attachment.DestinationPath;
        }
        finally
        {
            await EndDownloadAsync().ConfigureAwait(false);
        }
    }

    private async Task<TorrentPreparation> GetPreparationAsync(
        DownloadItem item,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(item.TorrentInfoHash)
            && _registeredPreparations.TryGetValue(
                item.TorrentInfoHash,
                out TorrentPreparation? registeredPreparation))
        {
            return registeredPreparation;
        }

        TorrentPreparation preparation = await PrepareAsync(
            item.Url,
            item.CustomHeaders,
            cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(item.TorrentInfoHash)
            && !string.Equals(item.TorrentInfoHash, preparation.InfoHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Torrent metadata does not match the saved info-hash.");
        }

        item.TorrentInfoHash = preparation.InfoHash;
        return preparation;
    }

    private async Task<TorrentAttachment> AttachAsync(
        TorrentPreparation preparation,
        int fileIndex,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        if (fileIndex < 0
            || !preparation.Files.Any(file => file.Index == fileIndex))
        {
            throw new InvalidDataException("The selected torrent file no longer exists.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_batches.TryGetValue(preparation.InfoHash, out TorrentBatch? batch))
            {
                ClientEngine engine = GetOrCreateEngine();
                string staging = Path.Combine(_stagingRoot, preparation.InfoHash);
                Directory.CreateDirectory(staging);
                var torrentSettings = new TorrentSettingsBuilder
                {
                    CreateContainingDirectory = false,
                    MaximumConnections = 60
                }.ToSettings();
                try
                {
                    TorrentManager manager = await engine.AddAsync(
                        preparation.Torrent,
                        staging,
                        torrentSettings).ConfigureAwait(false);
                    foreach (ITorrentManagerFile file in manager.Files)
                    {
                        await manager.SetFilePriorityAsync(file, Priority.DoNotDownload)
                            .ConfigureAwait(false);
                    }

                    batch = new TorrentBatch(preparation.InfoHash, manager);
                    _batches.Add(preparation.InfoHash, batch);
                }
                catch
                {
                    DisposeEngineIfIdle();
                    throw;
                }
            }

            if (batch.ActiveFileIndexes.Contains(fileIndex))
            {
                throw new InvalidOperationException("This torrent file is already being downloaded.");
            }

            ITorrentManagerFile selectedFile = batch.Manager.Files[fileIndex];
            destinationPath = ReserveDestinationPath(destinationPath);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)
                    ?? throw new InvalidDataException("The destination folder is invalid."));
                await batch.Manager.MoveFileAsync(selectedFile, destinationPath).ConfigureAwait(false);
                await batch.Manager.SetFilePriorityAsync(selectedFile, Priority.Normal)
                    .ConfigureAwait(false);
                batch.ActiveFileIndexes.Add(fileIndex);

                if (batch.Manager.State is TorrentState.Stopped or TorrentState.Paused)
                {
                    await batch.Manager.StartAsync().ConfigureAwait(false);
                }

                return new TorrentAttachment(batch, fileIndex, selectedFile, destinationPath);
            }
            catch
            {
                batch.ActiveFileIndexes.Remove(fileIndex);
                _reservedDestinationPaths.Remove(destinationPath);
                if (batch.ActiveFileIndexes.Count == 0)
                {
                    await RemoveBatchAsync(batch).ConfigureAwait(false);
                }

                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task DetachAsync(TorrentAttachment attachment)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            TorrentBatch batch = attachment.Batch;
            if (!batch.ActiveFileIndexes.Remove(attachment.FileIndex))
            {
                return;
            }


            _reservedDestinationPaths.Remove(attachment.DestinationPath);

            try
            {
                await batch.Manager.SetFilePriorityAsync(
                    attachment.File,
                    Priority.DoNotDownload).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Torrent] Could not disable file: {ex.Message}");
            }

            if (batch.ActiveFileIndexes.Count == 0)
            {
                await RemoveBatchAsync(batch).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Torrent] Could not detach file: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    // Must be called while _gate is held.
    private async Task RemoveBatchAsync(TorrentBatch batch)
    {
        try
        {
            await batch.Manager.StopAsync(StopTimeout).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Torrent] Could not stop manager: {ex.Message}");
        }

        try
        {
            if (_engine is not null)
            {
                await _engine.RemoveAsync(batch.Manager, RemoveMode.KeepAllData)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Torrent] Could not remove manager: {ex.Message}");
        }

        _batches.Remove(batch.InfoHash);
        DisposeEngineIfIdle();
    }

    private string ReserveDestinationPath(string requestedPath)
    {
        string fullPath = Path.GetFullPath(requestedPath);
        if (_reservedDestinationPaths.Add(fullPath))
        {
            return fullPath;
        }

        string? folder = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(folder))
        {
            throw new InvalidDataException("The destination folder is invalid.");
        }

        string stem = Path.GetFileNameWithoutExtension(fullPath);
        string extension = Path.GetExtension(fullPath);
        for (int suffix = 1; suffix < int.MaxValue; suffix++)
        {
            string candidate = Path.Combine(folder, $"{stem} ({suffix}){extension}");
            if (!File.Exists(candidate) && _reservedDestinationPaths.Add(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("A unique destination file name could not be reserved.");
    }

    private async Task<ClientEngine> RentMetadataEngineAsync(
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ClientEngine engine = GetOrCreateEngine();
            _metadataEngineUsers++;
            return engine;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task BeginDownloadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            _activeDownloads++;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task BeginPreparationAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            _activePreparations++;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EndDownloadAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_activeDownloads > 0)
            {
                _activeDownloads--;
            }

            if (_activeDownloads == 0)
            {
                _downloadsDrained?.TrySetResult();
                _downloadsDrained = null;
            }

            DisposeEngineIfIdle();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EndPreparationAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_activePreparations > 0)
            {
                _activePreparations--;
            }

            if (_activePreparations == 0)
            {
                _preparationsDrained?.TrySetResult();
                _preparationsDrained = null;
            }

            DisposeEngineIfIdle();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ReturnMetadataEngineAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_metadataEngineUsers > 0)
            {
                _metadataEngineUsers--;
            }

            DisposeEngineIfIdle();
        }
        finally
        {
            _gate.Release();
        }
    }

    // Must be called while _gate is held. Constructing the service itself does
    // not load MonoTorrent resources, open sockets or start background work.
    private ClientEngine GetOrCreateEngine()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (_engine is not null)
        {
            return _engine;
        }

        Directory.CreateDirectory(_cacheRoot);
        Directory.CreateDirectory(_stagingRoot);
        var settings = new EngineSettingsBuilder
        {
            AllowLocalPeerDiscovery = true,
            // This engine is short-lived and is created only when a torrent is
            // actually used. UPnP/NAT-PMP discovery can block manager startup
            // before trackers or DHT begin, and is unnecessary for metadata.
            AllowPortForwarding = false,
            AutoSaveLoadDhtCache = true,
            AutoSaveLoadFastResume = true,
            AutoSaveLoadMagnetLinkMetadata = false,
            CacheDirectory = _cacheRoot,
            DhtEndPoint = new IPEndPoint(IPAddress.Any, 0),
            // Prefer the IPv4 path used by DHT and the majority of trackers.
            // A disabled/broken IPv6 stack must not prevent engine startup.
            ListenEndPoints = new Dictionary<string, IPEndPoint>
            {
                ["ipv4"] = new(IPAddress.Any, 0)
            },
            UsePartialFiles = false
        }.ToSettings();
        _engine = new ClientEngine(settings);
        return _engine;
    }

    // Must be called while _gate is held. A metadata request or live manager
    // pins the engine; otherwise its sockets, caches and worker resources are
    // released immediately instead of being retained for the Core lifetime.
    private void DisposeEngineIfIdle()
    {
        if (_engine is null || _metadataEngineUsers != 0 || _batches.Count != 0)
        {
            return;
        }

        ClientEngine engine = _engine;
        _engine = null;
        _reservedDestinationPaths.Clear();
        try { engine.Dispose(); }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Torrent] Could not dispose idle engine: {ex.Message}");
        }
    }

    private static async Task<byte[]> DownloadTorrentFileAsync(
        string source,
        Dictionary<string, string>? headers,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("A torrent source must be an http/https URL or magnet link.");
        }

        using DownloadHttpClientLease lease = DownloadHttpClientFactory.Create(headers);
        using HttpResponseMessage response = await lease.Client.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumMetadataBytes)
        {
            throw new InvalidDataException("The torrent metadata file is too large.");
        }

        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var output = new MemoryStream();
        byte[] buffer = new byte[16 * 1024];
        while (true)
        {
            int read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > MaximumMetadataBytes)
            {
                throw new InvalidDataException("The torrent metadata file is too large.");
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private async Task<byte[]> DownloadMagnetMetadataAsync(
        MagnetLink magnet,
        CancellationToken cancellationToken)
    {
        ClientEngine engine = await RentMetadataEngineAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            Task<ReadOnlyMemory<byte>> metadataTask = engine
                .DownloadMetadataAsync(magnet, cancellationToken);
            try
            {
                // MonoTorrent 3.0.2 only observes the token while waiting for
                // MetadataReceived. Its StartAsync and cleanup paths can still
                // wait indefinitely, so bound the complete operation as well.
                ReadOnlyMemory<byte> result = await metadataTask
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                return result.ToArray();
            }
            catch
            {
                ObserveBackgroundFailure(metadataTask);
                throw;
            }
        }
        finally
        {
            await ReturnMetadataEngineAsync().ConfigureAwait(false);
        }
    }

    private static async Task<byte[]?> TryDownloadExactSourceAsync(
        string source,
        MagnetLink magnet,
        CancellationToken cancellationToken)
    {
        int queryIndex = source.IndexOf('?');
        if (queryIndex < 0 || queryIndex == source.Length - 1)
        {
            return null;
        }

        foreach (string part in source[(queryIndex + 1)..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = part.IndexOf('=');
            if (separator <= 0
                || !part[..separator].Equals("xs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string exactSource;
            try
            {
                exactSource = Uri.UnescapeDataString(part[(separator + 1)..]
                    .Replace('+', ' ')).Trim();
            }
            catch (UriFormatException)
            {
                continue;
            }

            if (!Uri.TryCreate(exactSource, UriKind.Absolute, out Uri? uri)
                || uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                continue;
            }

            for (int attempt = 1; attempt <= ExactSourceAttemptCount; attempt++)
            {
                using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
                attemptTimeout.CancelAfter(ExactSourceAttemptTimeout);

                try
                {
                    byte[] metadata = await DownloadTorrentFileAsync(
                        exactSource,
                        headers: null,
                        attemptTimeout.Token).ConfigureAwait(false);
                    Torrent torrent = Torrent.Load(metadata);
                    if (torrent.InfoHashes == magnet.InfoHashes)
                    {
                        return metadata;
                    }

                    Debug.WriteLine(
                        $"[Torrent] Ignored exact source with a mismatched info-hash: {uri}");
                    break;
                }
                catch (OperationCanceledException) when (
                    attemptTimeout.IsCancellationRequested
                    && !cancellationToken.IsCancellationRequested)
                {
                    Debug.WriteLine(
                        $"[Torrent] Exact source attempt {attempt} timed out: {uri}");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // An exact source is an optional shortcut. Retry transient
                    // HTTP failures, then continue with tracker/DHT discovery.
                    Debug.WriteLine(
                        $"[Torrent] Exact source attempt {attempt} failed '{uri}': {ex.Message}");
                }
            }
        }

        return null;
    }

    private static void ObserveBackgroundFailure(Task task)
    {
        if (task.IsCompleted)
        {
            _ = task.Exception;
            return;
        }

        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously
                | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try { await _shutdown.CancelAsync().ConfigureAwait(false); }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Torrent] Could not signal shutdown: {ex.Message}");
        }

        Task downloadsDrained;
        Task preparationsDrained;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_activeDownloads == 0)
            {
                downloadsDrained = Task.CompletedTask;
            }
            else
            {
                _downloadsDrained ??= new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                downloadsDrained = _downloadsDrained.Task;
            }

            if (_activePreparations == 0)
            {
                preparationsDrained = Task.CompletedTask;
            }
            else
            {
                _preparationsDrained ??= new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                preparationsDrained = _preparationsDrained.Task;
            }
        }
        finally
        {
            _gate.Release();
        }

        await Task.WhenAll(downloadsDrained, preparationsDrained).ConfigureAwait(false);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (TorrentBatch batch in _batches.Values.ToArray())
            {
                try
                {
                    await batch.Manager.StopAsync(StopTimeout).ConfigureAwait(false);
                    if (_engine is not null)
                    {
                        await _engine.RemoveAsync(batch.Manager, RemoveMode.KeepAllData)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Torrent] Shutdown '{batch.InfoHash}': {ex.Message}");
                }
            }

            _batches.Clear();
            _metadataEngineUsers = 0;
            DisposeEngineIfIdle();
            _registeredPreparations.Clear();
            lock (_preparationLocksSync)
            {
                foreach (PreparationLockEntry preparationLock in _preparationLocks.Values)
                {
                    preparationLock.Semaphore.Dispose();
                }

                _preparationLocks.Clear();
            }
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
            _shutdown.Dispose();
        }
    }

    private sealed class TorrentBatch
    {
        public TorrentBatch(string infoHash, TorrentManager manager)
        {
            InfoHash = infoHash;
            Manager = manager;
        }

        public string InfoHash { get; }
        public TorrentManager Manager { get; }
        public HashSet<int> ActiveFileIndexes { get; } = [];
    }

    private sealed class PreparationLockEntry
    {
        public PreparationLockEntry(string key)
        {
            Key = key;
        }

        public string Key { get; }
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int Users { get; set; }
    }

    private sealed record TorrentAttachment(
        TorrentBatch Batch,
        int FileIndex,
        ITorrentManagerFile File,
        string DestinationPath);
}
