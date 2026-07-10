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

namespace PDownloader.Core.Download.Hls;

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
        YtDlpService.HlsFragmentsResult fragmentResult,
        string outputFolder,
        string fileStem,
        string tempDirectory,
        int preferredConcurrency,
        Action<long, double> reportProgress,
        Action mergingStarted,
        CancellationToken cancellationToken)
    {
        List<string> urls = fragmentResult.FragmentUrls;
        int fragmentCount = urls.Count;
        var bytesPerFragment = new long[fragmentCount];
        var tempPaths = new string[fragmentCount];

        using var monitor = new DownloadProgressMonitor(
            () => bytesPerFragment.Sum(),
            reportProgress);
        monitor.Start();

        int concurrency = Math.Clamp(preferredConcurrency, 1, 16);
        using var semaphore = new SemaphoreSlim(concurrency);

        try
        {
            Task[] tasks = Enumerable.Range(0, fragmentCount)
                .Select(index => DownloadFragmentGuardedAsync(
                    urls[index],
                    index,
                    tempDirectory,
                    tempPaths,
                    bytesPerFragment,
                    semaphore,
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

        await MergeFragmentsAsync(tempPaths, finalPath, cancellationToken);
        return finalPath;
    }

    private async Task DownloadFragmentGuardedAsync(
        string url,
        int index,
        string tempDirectory,
        string[] tempPaths,
        long[] bytesPerFragment,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            string tempPath = Path.Combine(tempDirectory, $"frag_{index:D5}.part");
            tempPaths[index] = tempPath;
            await DownloadFragmentWithRetryAsync(
                url,
                tempPath,
                index,
                bytesPerFragment,
                cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task DownloadFragmentWithRetryAsync(
        string url,
        string tempPath,
        int index,
        long[] bytesPerFragment,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Exchange(ref bytesPerFragment[index], 0);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using HttpResponseMessage response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                response.EnsureSuccessStatusCode();

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
                }

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
                    await Task.Delay(300 * attempt, cancellationToken);
                }
            }
        }

        throw new InvalidOperationException(
            $"Tải fragment HLS thất bại sau {MaxRetries} lần thử: {url}",
            lastException);
    }

    private static async Task MergeFragmentsAsync(
        IReadOnlyList<string> tempPaths,
        string finalPath,
        CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(finalPath);
        if (directory != null)
        {
            Directory.CreateDirectory(directory);
        }

        string mergingPath = finalPath + ".merging";

        try
        {
            await using (var output = new FileStream(
                mergingPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                foreach (string tempPath in tempPaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    await using (var input = new FileStream(
                        tempPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read))
                    {
                        await input.CopyToAsync(output, cancellationToken);
                    }

                    TryDeleteFragment(tempPath);
                }
            }

            File.Move(mergingPath, finalPath, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(mergingPath))
                {
                    File.Delete(mergingPath);
                }
            }
            catch
            {
                // Best effort cleanup.
            }

            throw;
        }
    }

    private static void TryDeleteFragment(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HLS] Không thể xóa fragment ngay sau khi ghép: {ex.Message}");
        }
    }
}
