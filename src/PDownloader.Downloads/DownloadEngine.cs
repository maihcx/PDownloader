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

public class DownloadEngine
{
    private readonly DownloadItem _item;
    private readonly IProgress<DownloadProgress> _progress;
    private readonly CancellationToken _cancellationToken;
    private readonly DownloadHttpClientLease _httpClientLease;
    private readonly DownloadPathService _pathService;
    private readonly MultiSegmentDownloadService _multiSegmentDownloader;
    private readonly HlsDownloadHandler _hlsHandler;
    private readonly YoutubeDownloadHandler _youtubeHandler;
    private readonly FfmpegMuxer _ffmpegMuxer;

    internal DownloadEngine(
        DownloadItem item,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken,
        DownloadPathService pathService,
        YtDlpService ytDlpService,
        FfmpegMuxer ffmpegMuxer)
    {
        _item = item;
        _progress = progress;
        _cancellationToken = cancellationToken;

        _httpClientLease = DownloadHttpClientFactory.Create(item.CustomHeaders);
        _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
        _ffmpegMuxer = ffmpegMuxer ?? throw new ArgumentNullException(nameof(ffmpegMuxer));
        _multiSegmentDownloader = new MultiSegmentDownloadService(_httpClientLease.Client);
        _hlsHandler = new HlsDownloadHandler(
            item,
            _httpClientLease.Client,
            _pathService,
            ytDlpService,
            ReportProgress,
            ReportMergeProgress,
            ReportThreadProgress);
        _youtubeHandler = new YoutubeDownloadHandler(
            item,
            _pathService,
            ytDlpService,
            _ffmpegMuxer,
            ReportProgress,
            ReportMergeProgress,
            ReportThreadProgress);
    }

    public async Task RunAsync()
    {
        string tempDirectory = _pathService.GetTempDirectory(_item);
        Directory.CreateDirectory(tempDirectory);

        try
        {
            if (await TryRecoverPendingMergeAsync(tempDirectory))
            {
                return;
            }

            _item.IsMergeProgressActive = false;

            if (_item.IsYoutube)
            {
                if (await _hlsHandler.TryHandleAsync(tempDirectory, _cancellationToken))
                {
                    return;
                }

                await _youtubeHandler.RunAsync(tempDirectory, _cancellationToken);
                return;
            }

            if (await _hlsHandler.TryHandleAsync(tempDirectory, _cancellationToken))
            {
                return;
            }

            await RunHttpDownloadAsync(tempDirectory);
        }
        finally
        {
            _httpClientLease.Dispose();
        }
    }

    public static Task<string?> GetRemoteFileNameAsync(string url) =>
        HttpDownloadProbe.GetRemoteFileNameAsync(url);

    private async Task<bool> TryRecoverPendingMergeAsync(string tempDirectory)
    {
        MergeRecoveryManifest? manifest = MergeRecoveryStore.TryLoad(tempDirectory);
        if (manifest == null)
        {
            return false;
        }

        _item.MergeMode = manifest.FileMergeMode;
        _item.Status = DownloadStatus.Merging;
        _item.ErrorMessage = string.Empty;
        _item.SpeedBps = 0;
        ReportMergeProgress(0);

        string finalPath = manifest.Kind switch
        {
            MergeRecoveryKind.Concatenate => await new RecoverableFileMerger().RetryAsync(
                manifest,
                ReportMergeProgress,
                ApplyFileHashes,
                _cancellationToken),
            MergeRecoveryKind.FfmpegMux => await _ffmpegMuxer.RetryAsync(
                manifest,
                ReportMergeProgress,
                _cancellationToken),
            _ => throw new InvalidOperationException(
                $"Merge-style recovery is not supported: {manifest.Kind}.")
        };

        CompleteRecoveredMerge(finalPath);
        DownloadPathService.CleanupTemp(tempDirectory);
        return true;
    }

    private void CompleteRecoveredMerge(string finalPath)
    {
        long fileLength = new FileInfo(finalPath).Length;
        _item.FileName = Path.GetFileName(finalPath);
        _item.SavePath = finalPath;
        _item.TotalBytes = Math.Max(_item.TotalBytes, fileLength);
        ReportProgress(_item.TotalBytes, 0);
        _item.MergeProgress = 100;
        _item.Status = DownloadStatus.Completed;
        _item.EndTime = DateTime.Now;
    }

    private async Task RunHttpDownloadAsync(string tempDirectory)
    {
        string probeUrl = string.IsNullOrWhiteSpace(_item.ResolvedUrl)
            ? _item.Url
            : _item.ResolvedUrl;

        DownloadProbeResult probe = await _multiSegmentDownloader.ProbeAsync(
            probeUrl,
            _cancellationToken);

        if (probe.TotalBytes <= 0
            && !string.Equals(probeUrl, _item.Url, StringComparison.Ordinal))
        {
            probe = await _multiSegmentDownloader.ProbeAsync(
                _item.Url,
                _cancellationToken);
        }

        _item.ResolvedUrl = probe.EffectiveUrl;

        if (string.IsNullOrWhiteSpace(_item.FileName))
        {
            _item.FileName = probe.SuggestedFileName;
        }

        _item.TotalBytes = probe.TotalBytes;
        _item.Status = DownloadStatus.Downloading;
        _item.StartTime = DateTime.Now;

        string finalPath = _pathService.GetFinalPath(_item);
        await _multiSegmentDownloader.DownloadAsync(
            probe.EffectiveUrl,
            finalPath,
            tempDirectory,
            probe,
            _item.Threads,
            progressBaseOffset: 0,
            reportProgress: ReportProgress,
            reportThreadProgress: progress => ReportThreadProgress("File", progress),
            mergingStarted: () =>
            {
                _item.Status = DownloadStatus.Merging;
                ReportMergeProgress(0);
            },
            reportMergeProgress: ReportMergeProgress,
            reportFileHashes: ApplyFileHashes,
            fileMergeMode: _item.MergeMode,
            cancellationToken: _cancellationToken);

        _cancellationToken.ThrowIfCancellationRequested();
        _item.SavePath = finalPath;

        DownloadPathService.CleanupTemp(tempDirectory);

        long fileLength = new FileInfo(finalPath).Length;
        _item.TotalBytes = probe.TotalBytes > 0 ? probe.TotalBytes : fileLength;
        ReportProgress(_item.TotalBytes, 0);
        _item.Status = DownloadStatus.Completed;
        _item.EndTime = DateTime.Now;
    }

    private void ApplyFileHashes(FileHashResult hashes)
    {
        _item.Md5Hash = hashes.Md5;
        _item.Sha1Hash = hashes.Sha1;
        _item.Sha256Hash = hashes.Sha256;
    }

    private void ReportProgress(long downloadedBytes, double speedBps)
    {
        _item.IsMergeProgressActive = false;
        _item.DownloadedBytes = downloadedBytes;
        _item.SpeedBps = speedBps;
        _progress.Report(new DownloadProgress(downloadedBytes, speedBps));
    }

    private void ReportMergeProgress(double progressPercent)
    {
        _item.IsMergeProgressActive = true;
        _item.MergeProgress = progressPercent;
        _item.SpeedBps = 0;

        _progress.Report(new DownloadProgress(_item.DownloadedBytes, 0));
    }

    private void ReportThreadProgress(
        string stage,
        IReadOnlyList<DownloadThreadProgress> progress)
    {
        _item.SetThreadProgress(stage, progress);
    }
}
