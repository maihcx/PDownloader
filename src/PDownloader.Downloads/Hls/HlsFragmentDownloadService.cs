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

namespace PDownloader.Downloads.Hls;

internal sealed class HlsFragmentDownloadService
{
    private const int BufferSize = 81920;
    private const int MaxRetries = 3;

    private readonly HttpClient _httpClient;

    public HlsFragmentDownloadService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> DownloadAsync(
        HlsFragmentsResult fragmentResult,
        string outputFolder,
        string fileStem,
        string tempDirectory,
        int preferredConcurrency,
        Action<long, double> reportProgress,
        Action<IReadOnlyList<DownloadThreadProgress>>? reportThreadProgress,
        Action mergingStarted,
        Action<double>? reportMergeProgress,
        Action<FileHashResult>? reportFileHashes,
        FileMergeMode fileMergeMode,
        CancellationToken cancellationToken,
        Action<double>? reportDownloadProgress = null,
        Action<long, bool>? reportTotalBytes = null)
    {
        List<string> urls = fragmentResult.FragmentUrls;
        int fragmentCount = urls.Count;
        var bytesPerFragment = new long[fragmentCount];
        var totalBytesPerFragment = new long[fragmentCount];
        var completedFragments = new int[fragmentCount];
        var tempPaths = new string[fragmentCount];
        bool useDurations = fragmentResult.FragmentDurations.Count == fragmentCount
            && fragmentResult.FragmentDurations.All(duration => double.IsFinite(duration) && duration > 0);

        int concurrency = Math.Clamp(preferredConcurrency, 1, 16);
        concurrency = Math.Min(concurrency, Math.Max(1, fragmentCount));

        HlsWorkerState[] workers = Enumerable.Range(0, concurrency)
            .Select(index => new HlsWorkerState(index))
            .ToArray();
        var threadTracker = new HlsWorkerProgressTracker(workers, fragmentCount);

        void PublishProgress(long downloadedBytes, double speedBps)
        {
            double completedUnits = 0;
            double knownBytes = 0, completedBytes = 0, completedWeight = 0, unknownWeight = 0;
            bool allSizesKnown = true;
            for (int index = 0; index < fragmentCount; index++)
            {
                bool completed = Volatile.Read(ref completedFragments[index]) != 0;
                long total = Interlocked.Read(ref totalBytesPerFragment[index]);
                long downloaded = Interlocked.Read(ref bytesPerFragment[index]);
                double weight = useDurations ? fragmentResult.FragmentDurations[index] : 1;
                if (completed)
                {
                    completedUnits++;
                    knownBytes += downloaded;
                    completedBytes += downloaded;
                    completedWeight += weight;
                }
                else
                {
                    if (total > 0)
                    {
                        knownBytes += total;
                        // Reserve completion until the fragment has finished successfully.
                        completedUnits += Math.Clamp(downloaded / (double)total, 0, 0.99);
                    }
                    else
                    {
                        allSizesKnown = false;
                        unknownWeight += weight;
                    }
                }
            }
            // No extra HEAD requests: refine from successful segments and headers
            // obtained during normal downloads. Never extrapolate partial/retried data.
            MediaSizeEstimate size = fragmentResult.Size;
            if (allSizesKnown)
                size = new(MediaSizeEstimate.ToBytes(knownBytes), false);
            else if (completedWeight > 0 && completedBytes > 0
                && (size.Bytes <= 0 || size.IsEstimated))
                size = new(MediaSizeEstimate.ToBytes(
                    knownBytes + completedBytes / completedWeight * unknownWeight), true);

            long totalBytes = size.Bytes > 0 ? Math.Max(size.Bytes, downloadedBytes) : 0;
            reportTotalBytes?.Invoke(totalBytes, size.IsEstimated);
            reportDownloadProgress?.Invoke(fragmentCount > 0 ? completedUnits / fragmentCount * 100 : 0);
            reportThreadProgress?.Invoke(threadTracker.Capture());
            reportProgress(downloadedBytes, speedBps);
        }

        using var monitor = new DownloadProgressMonitor(
            () => bytesPerFragment.Sum(),
            PublishProgress);

        PublishProgress(0, 0);
        monitor.Start();

        int nextFragmentIndex = -1;
        try
        {
            Task[] tasks = workers
                .Select(worker => DownloadWorkerAsync(
                    worker,
                    urls,
                    tempDirectory,
                    tempPaths,
                    bytesPerFragment,
                    totalBytesPerFragment,
                    completedFragments,
                    () => Interlocked.Increment(ref nextFragmentIndex),
                    cancellationToken))
                .ToArray();

            await Task.WhenAll(tasks);
            monitor.ReportFinal();
        }
        finally
        {
            monitor.Stop();
        }

        mergingStarted();

        string extension = string.IsNullOrWhiteSpace(fragmentResult.Ext)
            ? "ts"
            : fragmentResult.Ext;
        string finalPath = DownloadPathService.UniqueFilePath(
            outputFolder,
            $"{fileStem}.{extension}");

        await MergeFragmentsAsync(
            tempPaths,
            finalPath,
            reportMergeProgress,
            reportFileHashes,
            fileMergeMode,
            cancellationToken);
        return finalPath;
    }

    private async Task DownloadWorkerAsync(
        HlsWorkerState worker,
        IReadOnlyList<string> urls,
        string tempDirectory,
        string[] tempPaths,
        long[] bytesPerFragment,
        long[] totalBytesPerFragment,
        int[] completedFragments,
        Func<int> getNextFragmentIndex,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int index = getNextFragmentIndex();
            if (index >= urls.Count)
            {
                worker.MarkCompleted();
                return;
            }

            string tempPath = Path.Combine(tempDirectory, $"frag_{index:D5}.part");
            tempPaths[index] = tempPath;
            worker.BeginFragment(index);

            await DownloadFragmentWithRetryAsync(
                urls[index],
                tempPath,
                index,
                bytesPerFragment,
                totalBytesPerFragment,
                worker,
                cancellationToken);
            Volatile.Write(ref completedFragments[index], 1);
        }
    }

    private async Task DownloadFragmentWithRetryAsync(
        string url,
        string tempPath,
        int index,
        long[] bytesPerFragment,
        long[] totalBytesPerFragment,
        HlsWorkerState worker,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Exchange(ref bytesPerFragment[index], 0);
            Interlocked.Exchange(ref totalBytesPerFragment[index], 0);
            worker.BeginAttempt();

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using HttpResponseMessage response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                response.EnsureSuccessStatusCode();

                long totalBytes = Math.Max(0, response.Content.Headers.ContentLength ?? 0);
                Interlocked.Exchange(ref totalBytesPerFragment[index], totalBytes);
                worker.SetCurrentTotalBytes(totalBytes);

                await using var output = new FileStream(
                    tempPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None);
                await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);

                byte[] buffer = new byte[BufferSize];
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    Interlocked.Add(ref bytesPerFragment[index], read);
                    worker.AddBytes(read);
                }

                worker.CompleteCurrentFragment();
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (attempt < MaxRetries)
                {
                    worker.MarkRetrying();
                    await Task.Delay(300 * attempt, cancellationToken);
                }
            }
        }

        worker.MarkFailed();
        throw new InvalidOperationException(
            $"Tải fragment HLS thất bại sau {MaxRetries} lần thử: {url}",
            lastException);
    }

    private static async Task MergeFragmentsAsync(
        IReadOnlyList<string> tempPaths,
        string finalPath,
        Action<double>? reportProgress,
        Action<FileHashResult>? reportFileHashes,
        FileMergeMode fileMergeMode,
        CancellationToken cancellationToken)
    {
        var merger = new RecoverableFileMerger();
        await merger.MergeAsync(
            tempPaths,
            finalPath,
            reportProgress,
            reportFileHashes,
            fileMergeMode,
            cancellationToken);
    }

    private sealed class HlsWorkerState
    {
        private int _state = (int)DownloadThreadState.Waiting;
        private int _currentFragmentIndex = -1;
        private long _currentDownloadedBytes;
        private long _currentTotalBytes;
        private long _lifetimeDownloadedBytes;

        public HlsWorkerState(int index)
        {
            Index = index;
        }

        public int Index { get; }

        public DownloadThreadState State =>
            (DownloadThreadState)Volatile.Read(ref _state);

        public int CurrentFragmentIndex =>
            Volatile.Read(ref _currentFragmentIndex);

        public long CurrentDownloadedBytes =>
            Interlocked.Read(ref _currentDownloadedBytes);

        public long CurrentTotalBytes =>
            Interlocked.Read(ref _currentTotalBytes);

        public long LifetimeDownloadedBytes =>
            Interlocked.Read(ref _lifetimeDownloadedBytes);

        public void BeginFragment(int fragmentIndex)
        {
            Volatile.Write(ref _currentFragmentIndex, fragmentIndex);
            Interlocked.Exchange(ref _currentDownloadedBytes, 0);
            Interlocked.Exchange(ref _currentTotalBytes, 0);
            Volatile.Write(ref _state, (int)DownloadThreadState.Waiting);
        }

        public void BeginAttempt()
        {
            Interlocked.Exchange(ref _currentDownloadedBytes, 0);
            Interlocked.Exchange(ref _currentTotalBytes, 0);
            Volatile.Write(ref _state, (int)DownloadThreadState.Downloading);
        }

        public void SetCurrentTotalBytes(long value) =>
            Interlocked.Exchange(ref _currentTotalBytes, Math.Max(0, value));

        public void AddBytes(int count)
        {
            Interlocked.Add(ref _currentDownloadedBytes, count);
            Interlocked.Add(ref _lifetimeDownloadedBytes, count);
        }

        public void MarkRetrying() =>
            Volatile.Write(ref _state, (int)DownloadThreadState.Retrying);

        public void CompleteCurrentFragment() =>
            Volatile.Write(ref _state, (int)DownloadThreadState.Completed);

        public void MarkCompleted() =>
            Volatile.Write(ref _state, (int)DownloadThreadState.Completed);

        public void MarkFailed() =>
            Volatile.Write(ref _state, (int)DownloadThreadState.Failed);
    }

    private sealed class HlsWorkerProgressTracker
    {
        private readonly HlsWorkerState[] _workers;
        private readonly long[] _lastLifetimeBytes;
        private readonly int _fragmentCount;
        private long _lastTimestamp;

        public HlsWorkerProgressTracker(
            HlsWorkerState[] workers,
            int fragmentCount)
        {
            _workers = workers;
            _fragmentCount = fragmentCount;
            _lastLifetimeBytes = new long[workers.Length];
            _lastTimestamp = Stopwatch.GetTimestamp();
        }

        public IReadOnlyList<DownloadThreadProgress> Capture()
        {
            long now = Stopwatch.GetTimestamp();
            long timestampDelta = now - _lastTimestamp;
            double elapsedSeconds = timestampDelta > 0
                ? timestampDelta / (double)Stopwatch.Frequency
                : 0;

            var result = new DownloadThreadProgress[_workers.Length];

            for (int index = 0; index < _workers.Length; index++)
            {
                HlsWorkerState worker = _workers[index];
                long lifetimeBytes = worker.LifetimeDownloadedBytes;
                long byteDelta = lifetimeBytes - _lastLifetimeBytes[index];
                DownloadThreadState state = worker.State;
                double speedBps = state is DownloadThreadState.Completed or DownloadThreadState.Failed
                    ? 0
                    : byteDelta > 0 && elapsedSeconds > 0
                        ? byteDelta / elapsedSeconds
                        : 0;

                int fragmentIndex = worker.CurrentFragmentIndex;
                result[index] = new DownloadThreadProgress(
                    worker.Index,
                    worker.CurrentDownloadedBytes,
                    worker.CurrentTotalBytes,
                    speedBps,
                    state,
                    fragmentIndex >= 0 ? fragmentIndex + 1 : 0,
                    _fragmentCount);

                _lastLifetimeBytes[index] = lifetimeBytes;
            }

            _lastTimestamp = now;
            return result;
        }
    }
}
