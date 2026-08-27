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

namespace PDownloader.Downloads.Handlers;

internal sealed class HlsDownloadHandler
{
    private readonly DownloadItem _item;
    private readonly HlsPlaylistDetector _detector;
    private readonly HlsFragmentDownloadService _fragmentDownloader;
    private readonly YtDlpHlsDownloadService _ytDlpDownloader;
    private readonly DownloadPathService _pathService;
    private readonly Action<long, double> _reportProgress;
    private readonly Action<double> _reportMergeProgress;
    private readonly Action<string, IReadOnlyList<DownloadThreadProgress>> _reportThreadProgress;

    public HlsDownloadHandler(
        DownloadItem item,
        HttpClient httpClient,
        DownloadPathService pathService,
        Action<long, double> reportProgress,
        Action<double> reportMergeProgress,
        Action<string, IReadOnlyList<DownloadThreadProgress>> reportThreadProgress)
    {
        _item = item;
        _detector = new HlsPlaylistDetector(httpClient);
        _fragmentDownloader = new HlsFragmentDownloadService(httpClient);
        _ytDlpDownloader = new YtDlpHlsDownloadService();
        _pathService = pathService;
        _reportProgress = reportProgress;
        _reportMergeProgress = reportMergeProgress;
        _reportThreadProgress = reportThreadProgress;
    }

    public async Task<bool> TryHandleAsync(
        string tempDirectory,
        CancellationToken cancellationToken)
    {
        if (!await _detector.IsHlsPlaylistAsync(_item.Url, cancellationToken))
        {
            return false;
        }

        if (YtDlpService.Instance.FindYtDlp() == null)
        {
            SetError(
                "Detected an HLS (m3u8) playlist, but yt-dlp is required to download/merge it. " +
                "Place yt-dlp.exe next to PDownloader.Core.exe or add it to your PATH.");
            return true;
        }

        string outputFolder = _pathService.GetOutputFolder(_item);
        Directory.CreateDirectory(outputFolder);

        string fileStem = string.IsNullOrWhiteSpace(_item.FileName)
            ? DownloadPathService.SanitizeFileName(
                DownloadPathService.GuessFileName(_item.Url))
            : DownloadPathService.SanitizeFileName(
                Path.GetFileNameWithoutExtension(_item.FileName));

        string? referer = DownloadPathService.GetHeader(_item.CustomHeaders, "Referer");
        string? cookieHeader = DownloadPathService.GetHeader(_item.CustomHeaders, "Cookie");
        string? cookieJarJson = DownloadPathService.GetHeader(
            _item.CustomHeaders,
            "X-PDownloader-Cookie-Jar");
        string? userAgent = DownloadPathService.GetHeader(_item.CustomHeaders, "User-Agent");

        _item.Status = DownloadStatus.Connecting;

        try
        {
            HlsFragmentsResult? fragmentResult =
                await TryResolveFragmentsAsync(
                    referer,
                    cookieHeader,
                    cookieJarJson,
                    userAgent,
                    cancellationToken);

            _item.Status = DownloadStatus.Downloading;
            _item.StartTime = DateTime.Now;
            _item.TotalBytes = 0;

            string finalPath = fragmentResult is { FragmentUrls.Count: > 0 }
                ? await DownloadResolvedFragmentsAsync(
                    fragmentResult,
                    outputFolder,
                    fileStem,
                    tempDirectory,
                    cancellationToken)
                : await DownloadWithYtDlpAsync(
                    outputFolder,
                    fileStem,
                    tempDirectory,
                    referer,
                    cookieHeader,
                    cookieJarJson,
                    userAgent,
                    cancellationToken);

            Complete(finalPath);
            DownloadPathService.CleanupTemp(tempDirectory);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            bool mergeCanRetry = MergeRecoveryStore.HasPending(tempDirectory);
            SetError(mergeCanRetry
                ? ex.Message
                : "Unable to load HLS: " + ex.Message);
            return true;
        }
    }

    private async Task<HlsFragmentsResult?> TryResolveFragmentsAsync(
        string? referer,
        string? cookieHeader,
        string? cookieJarJson,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        try
        {
            return await YtDlpService.Instance.ResolveHlsFragmentsAsync(
                _item.Url,
                _item.FormatId,
                referer,
                cookieHeader,
                cookieJarJson,
                userAgent,
                _item.CustomHeaders,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private Task<string> DownloadResolvedFragmentsAsync(
        HlsFragmentsResult fragmentResult,
        string outputFolder,
        string fileStem,
        string tempDirectory,
        CancellationToken cancellationToken)
    {
        return _fragmentDownloader.DownloadAsync(
            fragmentResult,
            outputFolder,
            fileStem,
            tempDirectory,
            _item.Threads,
            _reportProgress,
            progress => _reportThreadProgress("HlsFragments", progress),
            () =>
            {
                _item.Status = DownloadStatus.Merging;
                _reportMergeProgress(0);
            },
            _reportMergeProgress,
            ApplyFileHashes,
            _item.MergeMode,
            cancellationToken);
    }

    private async Task<string> DownloadWithYtDlpAsync(
        string outputFolder,
        string fileStem,
        string tempDirectory,
        string? referer,
        string? cookieHeader,
        string? cookieJarJson,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        _item.SetProgressVisualizationUnsupported("YtDlp");
        _reportProgress(_item.DownloadedBytes, _item.SpeedBps);

        string uniqueMp4Path = DownloadPathService.UniqueFilePath(
            outputFolder,
            $"{fileStem}.mp4");
        string outputPathWithoutExtension = Path.Combine(
            Path.GetDirectoryName(uniqueMp4Path) ?? outputFolder,
            Path.GetFileNameWithoutExtension(uniqueMp4Path));

        long previousDownloadedBytes = 0;
        long previousTimestamp = Stopwatch.GetTimestamp();
        object progressSync = new();

        return await _ytDlpDownloader.DownloadAsync(
            _item.Url,
            _item.FormatId,
            tempDirectory,
            outputPathWithoutExtension,
            referer,
            cookieHeader,
            cookieJarJson,
            userAgent,
            _item.CustomHeaders,
            _item.Threads,
            (downloadedBytes, totalBytes, ytDlpSpeedBps) =>
            {
                lock (progressSync)
                {
                    long now = Stopwatch.GetTimestamp();
                    double elapsedSeconds =
                        (now - previousTimestamp) / (double)Stopwatch.Frequency;

                    double fallbackSpeedBps =
                        downloadedBytes >= previousDownloadedBytes
                        && elapsedSeconds > 0
                            ? (downloadedBytes - previousDownloadedBytes)
                                / elapsedSeconds
                            : 0;

                    previousDownloadedBytes = downloadedBytes;
                    previousTimestamp = now;

                    if (totalBytes > 0)
                    {
                        _item.TotalBytes = totalBytes;
                    }

                    double speedBps = ytDlpSpeedBps > 0
                        ? ytDlpSpeedBps
                        : fallbackSpeedBps;

                    _reportProgress(downloadedBytes, speedBps);
                }
            },
            cancellationToken);
    }

    private void ApplyFileHashes(FileHashResult hashes)
    {
        _item.Md5Hash = hashes.Md5;
        _item.Sha1Hash = hashes.Sha1;
        _item.Sha256Hash = hashes.Sha256;
    }

    private void Complete(string finalPath)
    {
        long fileLength = new FileInfo(finalPath).Length;
        _item.FileName = Path.GetFileName(finalPath);
        _item.SavePath = finalPath;
        _item.TotalBytes = fileLength;
        _reportProgress(fileLength, 0);
        _item.Status = DownloadStatus.Completed;
        _item.EndTime = DateTime.Now;
    }

    private void SetError(string message)
    {
        _item.Status = DownloadStatus.Error;
        _item.ErrorMessage = message;
        _item.SpeedBps = 0;
    }
}
