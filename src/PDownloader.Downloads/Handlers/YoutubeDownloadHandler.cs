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

internal sealed class YoutubeDownloadHandler
{
    private readonly DownloadItem _item;
    private readonly DownloadPathService _pathService;
    private readonly FfmpegMuxer _ffmpegMuxer;
    private readonly YtDlpService _ytDlpService;
    private readonly YtDlpHlsDownloadService _ytDlpFragmentedDownloader;
    private readonly Action<long, double> _reportProgress;
    private readonly Action<double> _reportMergeProgress;
    private readonly Action<string, IReadOnlyList<DownloadThreadProgress>> _reportThreadProgress;

    public YoutubeDownloadHandler(
        DownloadItem item,
        DownloadPathService pathService,
        YtDlpService ytDlpService,
        FfmpegMuxer ffmpegMuxer,
        Action<long, double> reportProgress,
        Action<double> reportMergeProgress,
        Action<string, IReadOnlyList<DownloadThreadProgress>> reportThreadProgress)
    {
        _item = item;
        _pathService = pathService;
        _ytDlpService = ytDlpService ?? throw new ArgumentNullException(nameof(ytDlpService));
        _ffmpegMuxer = ffmpegMuxer ?? throw new ArgumentNullException(nameof(ffmpegMuxer));
        _ytDlpFragmentedDownloader = new YtDlpHlsDownloadService(_ytDlpService);
        _reportProgress = reportProgress;
        _reportMergeProgress = reportMergeProgress;
        _reportThreadProgress = reportThreadProgress;
    }

    public async Task RunAsync(
        string tempDirectory,
        CancellationToken cancellationToken)
    {
        if (_ytDlpService.FindYtDlp() == null)
        {
            throw new ArgumentNullException("yt-dlp not found.");
        }

        string outputFolder = _pathService.GetOutputFolder(_item);
        Directory.CreateDirectory(outputFolder);

        string? referer = DownloadPathService.GetHeader(_item.CustomHeaders, "Referer");
        string? cookieHeader = DownloadPathService.GetHeader(_item.CustomHeaders, "Cookie");
        string? cookieJarJson = DownloadPathService.GetHeader(
            _item.CustomHeaders,
            "X-PDownloader-Cookie-Jar");
        string? userAgent = DownloadPathService.GetHeader(_item.CustomHeaders, "User-Agent");
        string fileStem = string.IsNullOrWhiteSpace(_item.FileName)
            ? DownloadPathService.SanitizeFileName(
                DownloadPathService.GuessFileName(_item.Url))
            : DownloadPathService.SanitizeFileName(
                Path.GetFileNameWithoutExtension(_item.FileName));

        _item.Status = DownloadStatus.Connecting;

        List<ResolvedStream> streams;
        try
        {
            streams = await _ytDlpService.ResolveDirectUrlsAsync(
                _item.Url,
                _item.FormatId ?? "bestvideo+bestaudio/best",
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
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Unable to resolve URL from yt-dlp: " + ex.Message,
                ex);
        }

        if (streams.Count == 0)
        {
            throw new Exception("yt-dlp does not return any stream to download.");
        }

        if (streams.Any(stream => !stream.IsDirectHttp))
        {
            await DownloadFragmentedFormatAsync(
                outputFolder,
                fileStem,
                tempDirectory,
                referer,
                cookieHeader,
                cookieJarJson,
                userAgent,
                cancellationToken);
            return;
        }

        _item.SetTotalBytes(streams.All(stream => stream.FilesizeApprox > 0)
            ? streams.Sum(stream => stream.FilesizeApprox)
            : 0, streams.Any(stream => stream.IsFilesizeEstimated));
        _item.Status = DownloadStatus.Downloading;
        _item.StartTime = DateTime.Now;

        List<DownloadedStreamFile> rawFiles = await DownloadStreamsAsync(
            streams,
            tempDirectory,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        _item.Status = DownloadStatus.Merging;
        _reportMergeProgress(0);

        string finalPath;
        if (rawFiles.Count == 1)
        {
            finalPath = MoveSingleStream(rawFiles[0], outputFolder, fileStem);
            _reportMergeProgress(100);
        }
        else
        {
            finalPath = await _ffmpegMuxer.MuxAsync(
                rawFiles,
                outputFolder,
                fileStem,
                _reportMergeProgress,
                _item.MergeMode,
                cancellationToken);
        }

        Complete(finalPath);
        DownloadPathService.CleanupTemp(tempDirectory);
    }

    private async Task DownloadFragmentedFormatAsync(
        string outputFolder,
        string fileStem,
        string tempDirectory,
        string? referer,
        string? cookieHeader,
        string? cookieJarJson,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        _item.Status = DownloadStatus.Downloading;
        _item.StartTime = DateTime.Now;
        _item.SetTotalBytes(0);
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

        string finalPath = await _ytDlpFragmentedDownloader.DownloadAsync(
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
            progress =>
            {
                lock (progressSync)
                {
                    long downloadedBytes = progress.DownloadedBytes;
                    double ytDlpSpeedBps = progress.SpeedBps;
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

                    _item.SetTotalBytes(progress.TotalBytes, progress.IsTotalEstimated);
                    _item.DownloadProgressPercent = progress.Percent;

                    _reportProgress(
                        downloadedBytes,
                        ytDlpSpeedBps > 0 ? ytDlpSpeedBps : fallbackSpeedBps);
                }
            },
            cancellationToken);

        long fileLength = new FileInfo(finalPath).Length;
        _item.FileName = Path.GetFileName(finalPath);
        _item.SavePath = finalPath;
        _item.SetTotalBytes(fileLength);
        _reportProgress(_item.TotalBytes, 0);
        _item.Status = DownloadStatus.Completed;
        _item.EndTime = DateTime.Now;
        DownloadPathService.CleanupTemp(tempDirectory);
    }

    private async Task<List<DownloadedStreamFile>> DownloadStreamsAsync(
        IReadOnlyList<ResolvedStream> streams,
        string tempDirectory,
        CancellationToken cancellationToken)
    {
        var files = new List<DownloadedStreamFile>(streams.Count);
        long progressBaseOffset = 0;
        long[] streamSizes = streams.Select(stream => Math.Max(0, stream.FilesizeApprox)).ToArray();
        bool[] estimatedSizes = streams.Select(stream => stream.IsFilesizeEstimated).ToArray();

        void UpdateTotalBytes()
        {
            // A partial sum is not the total size of a video + audio download.
            _item.SetTotalBytes(streamSizes.All(size => size > 0) ? streamSizes.Sum() : 0,
                estimatedSizes.Any(estimated => estimated));
        }

        foreach (ResolvedStream stream in streams)
        {
            string extension = string.IsNullOrWhiteSpace(stream.Ext) ? "bin" : stream.Ext;
            string kind = stream.HasVideo ? "video" : "audio";

            string rawPath = Path.Combine(tempDirectory, $"{kind}.{extension}");
            string segmentDirectory = Path.Combine(tempDirectory, $"{kind}_segs");

            Dictionary<string, string>? streamHeaders = MergeHeaders(
                _item.CustomHeaders,
                stream.HttpHeaders);

            using DownloadHttpClientLease streamClientLease =
                DownloadHttpClientFactory.Create(streamHeaders);
            var streamDownloader = new MultiSegmentDownloadService(
                streamClientLease.Client);

            string progressStage = stream.HasVideo ? "Video" : "Audio";
            int streamIndex = files.Count;

            void ReportStreamProgress(long downloadedBytes, double speedBps)
            {
                long currentBytes = Math.Max(0, downloadedBytes - progressBaseOffset);
                double fraction = streamSizes[streamIndex] > 0
                    ? Math.Clamp(currentBytes / (double)streamSizes[streamIndex], 0, 1)
                    : 0;
                _item.DownloadProgressPercent = (streamIndex + fraction) / streams.Count * 100;
                _reportProgress(downloadedBytes, speedBps);
            }

            DownloadProbeResult probe = await streamDownloader.ProbeAndDownloadAsync(
                stream.Url,
                rawPath,
                segmentDirectory,
                _item.Threads,
                progressBaseOffset,
                ReportStreamProgress,
                progress => _reportThreadProgress(progressStage, progress),
                () =>
                {
                    _item.Status = DownloadStatus.Merging;
                    _reportMergeProgress(0);
                },
                _reportMergeProgress,
                streams.Count == 1 ? ApplyFileHashes : null,
                _item.MergeMode,
                cancellationToken,
                reportProbe: result =>
                {
                    if (result.TotalBytes > 0)
                    {
                        streamSizes[streamIndex] = result.TotalBytes;
                        estimatedSizes[streamIndex] = false;
                    }

                    UpdateTotalBytes();
                });

            _item.Status = DownloadStatus.Downloading;
            DownloadContentInspector.EnsureDownloadedMediaFile(rawPath, stream);

            long actualLength = File.Exists(rawPath)
                ? new FileInfo(rawPath).Length
                : probe.TotalBytes;
            streamSizes[streamIndex] = actualLength;
            estimatedSizes[streamIndex] = false;
            progressBaseOffset += actualLength;
            UpdateTotalBytes();
            _item.DownloadProgressPercent = (streamIndex + 1.0) / streams.Count * 100;
            _reportProgress(progressBaseOffset, 0);
            files.Add(new DownloadedStreamFile(stream, rawPath));
            DownloadPathService.CleanupTemp(segmentDirectory);
        }

        if (_item.TotalBytes <= 0 || progressBaseOffset > _item.TotalBytes)
        {
            _item.SetTotalBytes(progressBaseOffset);
        }

        return files;
    }

    private void ApplyFileHashes(FileHashResult hashes)
    {
        _item.Md5Hash = hashes.Md5;
        _item.Sha1Hash = hashes.Sha1;
        _item.Sha256Hash = hashes.Sha256;
    }

    private static Dictionary<string, string>? MergeHeaders(
        Dictionary<string, string>? originalHeaders,
        Dictionary<string, string>? resolvedHeaders)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (originalHeaders != null)
        {
            foreach ((string key, string value) in originalHeaders)
            {
                if (!string.IsNullOrWhiteSpace(key)
                    && !string.IsNullOrWhiteSpace(value))
                {
                    merged[key] = value;
                }
            }
        }

        if (resolvedHeaders != null)
        {
            foreach ((string key, string value) in resolvedHeaders)
            {
                if (!string.IsNullOrWhiteSpace(key)
                    && !string.IsNullOrWhiteSpace(value))
                {
                    merged[key] = value;
                }
            }
        }

        return merged.Count == 0 ? null : merged;
    }

    private static string MoveSingleStream(
        DownloadedStreamFile file,
        string outputFolder,
        string fileStem)
    {
        string extension = string.IsNullOrWhiteSpace(file.Stream.Ext)
            ? "mp4"
            : file.Stream.Ext;
        string finalPath = DownloadPathService.UniqueFilePath(
            outputFolder,
            $"{fileStem}.{extension}");
        File.Move(file.Path, finalPath, overwrite: true);
        return finalPath;
    }

    private void Complete(string finalPath)
    {
        long finalLength = new FileInfo(finalPath).Length;

        _item.SetTotalBytes(finalLength);
        _item.FileName = Path.GetFileName(finalPath);
        _item.SavePath = finalPath;
        _reportProgress(_item.TotalBytes, 0);
        _item.Status = DownloadStatus.Completed;
        _item.EndTime = DateTime.Now;
    }
}
