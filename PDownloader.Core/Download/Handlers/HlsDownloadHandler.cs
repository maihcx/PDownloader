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

namespace PDownloader.Core.Download.Handlers;

internal sealed class HlsDownloadHandler
{
    private readonly DownloadItem _item;
    private readonly HlsPlaylistDetector _detector;
    private readonly HlsFragmentDownloadService _fragmentDownloader;
    private readonly YtDlpHlsDownloadService _ytDlpDownloader;
    private readonly DownloadPathService _pathService;
    private readonly Action<long, double> _reportProgress;

    public HlsDownloadHandler(
        DownloadItem item,
        HttpClient httpClient,
        DownloadPathService pathService,
        Action<long, double> reportProgress)
    {
        _item = item;
        _detector = new HlsPlaylistDetector(httpClient);
        _fragmentDownloader = new HlsFragmentDownloadService(httpClient);
        _ytDlpDownloader = new YtDlpHlsDownloadService();
        _pathService = pathService;
        _reportProgress = reportProgress;
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
                "Phát hiện đây là playlist HLS (m3u8) nhưng cần yt-dlp để tải/ghép. " +
                "Đặt yt-dlp.exe cạnh PDownloader.Core.exe hoặc thêm vào PATH.");
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

        _item.Status = DownloadStatus.Connecting;

        try
        {
            HlsFragmentsResult? fragmentResult =
                await TryResolveFragmentsAsync(referer, cookieHeader, cancellationToken);

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
            SetError("Không tải được HLS: " + ex.Message);
            return true;
        }
    }

    private async Task<HlsFragmentsResult?> TryResolveFragmentsAsync(
        string? referer,
        string? cookieHeader,
        CancellationToken cancellationToken)
    {
        try
        {
            return await YtDlpService.Instance.ResolveHlsFragmentsAsync(
                _item.Url,
                referer,
                cookieHeader,
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
            () => _item.Status = DownloadStatus.Merging,
            cancellationToken);
    }

    private async Task<string> DownloadWithYtDlpAsync(
        string outputFolder,
        string fileStem,
        string tempDirectory,
        string? referer,
        string? cookieHeader,
        CancellationToken cancellationToken)
    {
        string uniqueMp4Path = DownloadPathService.UniqueFilePath(
            outputFolder,
            $"{fileStem}.mp4");
        string outputPathWithoutExtension = Path.Combine(
            Path.GetDirectoryName(uniqueMp4Path) ?? outputFolder,
            Path.GetFileNameWithoutExtension(uniqueMp4Path));

        long downloadedBytes = 0;
        long totalBytes = 0;

        using var monitor = new DownloadProgressMonitor(
            () => Interlocked.Read(ref downloadedBytes),
            _reportProgress,
            afterReport: () =>
                _item.TotalBytes = Interlocked.Read(ref totalBytes));
        monitor.Start();

        try
        {
            string finalPath = await _ytDlpDownloader.DownloadAsync(
                _item.Url,
                tempDirectory,
                outputPathWithoutExtension,
                referer,
                cookieHeader,
                _item.Threads,
                (downloaded, total) =>
                {
                    Interlocked.Exchange(ref downloadedBytes, downloaded);
                    Interlocked.Exchange(ref totalBytes, total);
                },
                cancellationToken);

            monitor.ReportFinal();
            return finalPath;
        }
        finally
        {
            monitor.Stop();
        }
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
