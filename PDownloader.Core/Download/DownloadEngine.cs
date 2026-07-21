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

namespace PDownloader.Core.Download;

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

    public DownloadEngine(
        DownloadItem item,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        _item = item;
        _progress = progress;
        _cancellationToken = cancellationToken;

        _httpClientLease = DownloadHttpClientFactory.Create(item.CustomHeaders);
        _pathService = new DownloadPathService();
        _multiSegmentDownloader = new MultiSegmentDownloadService(_httpClientLease.Client);
        _hlsHandler = new HlsDownloadHandler(
            item,
            _httpClientLease.Client,
            _pathService,
            ReportProgress,
            ReportThreadProgress);
        _youtubeHandler = new YoutubeDownloadHandler(
            item,
            _pathService,
            ReportProgress,
            ReportThreadProgress);
    }

    public async Task RunAsync()
    {
        string tempDirectory = _pathService.GetTempDirectory(_item.Id);
        Directory.CreateDirectory(tempDirectory);

        try
        {
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

    public static void DeleteTempFiles(
        string id,
        string? savePath,
        string? fileName) =>
        DownloadPathService.DeleteTempFiles(id, savePath, fileName);

    public static Task<string?> GetRemoteFileNameAsync(string url) =>
        HttpDownloadProbe.GetRemoteFileNameAsync(url);

    private async Task RunHttpDownloadAsync(string tempDirectory)
    {
        string probeUrl = string.IsNullOrWhiteSpace(_item.ResolvedUrl)
            ? _item.Url
            : _item.ResolvedUrl;

        DownloadProbeResult probe = await _multiSegmentDownloader.ProbeAsync(
            probeUrl,
            _cancellationToken);

        // Mirror URLs (for example SourceForge) are pinned after the first redirect.
        // If a previously resolved mirror is no longer reachable, fall back to the
        // original URL so the provider can select a new mirror.
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
            mergingStarted: () => _item.Status = DownloadStatus.Merging,
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

    private void ReportProgress(long downloadedBytes, double speedBps)
    {
        _item.DownloadedBytes = downloadedBytes;
        _item.SpeedBps = speedBps;
        _progress.Report(new DownloadProgress(downloadedBytes, speedBps));
    }

    private void ReportThreadProgress(
        string stage,
        IReadOnlyList<DownloadThreadProgress> progress)
    {
        _item.SetThreadProgress(stage, progress);
    }
}
