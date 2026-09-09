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
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);

    private readonly ClientEngine _engine;
    private readonly string _stagingRoot;
    private readonly ConcurrentDictionary<string, TorrentPreparation> _preparations =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TorrentBatch> _batches =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _reservedDestinationPaths =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _preparationGate = new(1, 1);
    private int _disposed;

    public TorrentEngineService()
    {
        string cacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SM SOFT", "PDownloader", "TorrentCache");
        _stagingRoot = Path.Combine(cacheRoot, "staging");
        Directory.CreateDirectory(cacheRoot);
        Directory.CreateDirectory(_stagingRoot);

        EngineSettings settings = new EngineSettingsBuilder
        {
            AllowPortForwarding = true,
            AutoSaveLoadDhtCache = true,
            AutoSaveLoadFastResume = true,
            AutoSaveLoadMagnetLinkMetadata = true,
            CacheDirectory = cacheRoot,
            UsePartialFiles = false
        }.ToSettings();
        _engine = new ClientEngine(settings);
    }

    public async Task<TorrentPreparation> PrepareAsync(
        string source,
        Dictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        await _preparationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await PrepareCoreAsync(source, headers, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _preparationGate.Release();
        }
    }

    private async Task<TorrentPreparation> PrepareCoreAsync(
        string source,
        Dictionary<string, string>? headers,
        CancellationToken cancellationToken)
    {

        byte[] metadata;
        if (DownloadSource.IsMagnet(source))
        {
            if (!MagnetLink.TryParse(source.Trim(), out MagnetLink? magnet))
            {
                throw new InvalidDataException("The magnet link is invalid.");
            }

            string magnetInfoHash = magnet.InfoHashes.V1OrV2.ToHex();
            if (_preparations.TryGetValue(magnetInfoHash, out TorrentPreparation? cached))
            {
                return cached;
            }

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeoutSource.CancelAfter(MetadataDownloadTimeout);

            try
            {
                ReadOnlyMemory<byte> result = await _engine
                    .DownloadMetadataAsync(magnet, timeoutSource.Token)
                    .ConfigureAwait(false);
                metadata = result.ToArray();
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

        _preparations[preparation.InfoHash] = preparation;
        return preparation;
    }

    public void Register(TorrentPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        _preparations[preparation.InfoHash] = preparation;
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

        TorrentPreparation preparation = await GetPreparationAsync(item, cancellationToken)
            .ConfigureAwait(false);
        TorrentAttachment attachment = await AttachAsync(
            preparation,
            item.TorrentFileIndex,
            destinationPath,
            cancellationToken).ConfigureAwait(false);
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
                throw new InvalidDataException("Torrent file metadata does not match the saved download.");
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
                cancellationToken.ThrowIfCancellationRequested();
                if (attachment.Batch.Manager.State == TorrentState.Error)
                {
                    throw attachment.Batch.Manager.Error?.Exception
                        ?? new IOException("The torrent engine entered an error state.");
                }

                await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
                long bytes = Math.Min(attachment.File.Length, attachment.File.BytesDownloaded());
                long now = Stopwatch.GetTimestamp();
                double seconds = Stopwatch.GetElapsedTime(lastTick, now).TotalSeconds;
                double speed = seconds > 0 ? Math.Max(0, bytes - lastBytes) / seconds : 0;
                reportProgress(bytes, speed);
                lastBytes = bytes;
                lastTick = now;
            }

            cancellationToken.ThrowIfCancellationRequested();
            reportProgress(attachment.File.Length, 0);
        }
        finally
        {
            await DetachAsync(attachment).ConfigureAwait(false);
        }

        return attachment.DestinationPath;
    }

    private async Task<TorrentPreparation> GetPreparationAsync(
        DownloadItem item,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(item.TorrentInfoHash)
            && _preparations.TryGetValue(item.TorrentInfoHash, out TorrentPreparation? cached))
        {
            return cached;
        }

        await _preparationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        TorrentPreparation preparation;
        try
        {
            if (!string.IsNullOrWhiteSpace(item.TorrentInfoHash)
                && _preparations.TryGetValue(item.TorrentInfoHash, out cached))
            {
                return cached;
            }

            preparation = await PrepareCoreAsync(
                item.Url,
                item.CustomHeaders,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _preparationGate.Release();
        }
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
                string staging = Path.Combine(_stagingRoot, preparation.InfoHash);
                Directory.CreateDirectory(staging);
                TorrentSettings torrentSettings = new TorrentSettingsBuilder
                {
                    CreateContainingDirectory = false,
                    MaximumConnections = 60
                }.ToSettings();
                TorrentManager manager = await _engine.AddAsync(
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

            await batch.Manager.SetFilePriorityAsync(
                attachment.File,
                Priority.DoNotDownload).ConfigureAwait(false);
            if (batch.ActiveFileIndexes.Count == 0)
            {
                await batch.Manager.StopAsync(StopTimeout).ConfigureAwait(false);
                await _engine.RemoveAsync(batch.Manager, RemoveMode.KeepAllData)
                    .ConfigureAwait(false);
                _batches.Remove(batch.InfoHash);
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

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (TorrentBatch batch in _batches.Values.ToArray())
            {
                try
                {
                    await batch.Manager.StopAsync(StopTimeout).ConfigureAwait(false);
                    await _engine.RemoveAsync(batch.Manager, RemoveMode.KeepAllData)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Torrent] Shutdown '{batch.InfoHash}': {ex.Message}");
                }
            }

            _batches.Clear();
            _engine.Dispose();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
            _preparationGate.Dispose();
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

    private sealed record TorrentAttachment(
        TorrentBatch Batch,
        int FileIndex,
        ITorrentManagerFile File,
        string DestinationPath);
}
