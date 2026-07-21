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

internal sealed class YoutubeDownloadHandler
{
    private readonly DownloadItem _item;
    private readonly DownloadPathService _pathService;
    private readonly FfmpegMuxer _ffmpegMuxer;
    private readonly Action<long, double> _reportProgress;
    private readonly Action<string, IReadOnlyList<DownloadThreadProgress>> _reportThreadProgress;

    public YoutubeDownloadHandler(
        DownloadItem item,
        DownloadPathService pathService,
        Action<long, double> reportProgress,
        Action<string, IReadOnlyList<DownloadThreadProgress>> reportThreadProgress)
    {
        _item = item;
        _pathService = pathService;
        _ffmpegMuxer = new FfmpegMuxer();
        _reportProgress = reportProgress;
        _reportThreadProgress = reportThreadProgress;
    }

    public async Task RunAsync(
        string tempDirectory,
        CancellationToken cancellationToken)
    {
        if (YtDlpService.Instance.FindYtDlp() == null)
        {
            SetError("yt-dlp không tìm thấy.");
            return;
        }

        string outputFolder = _pathService.GetOutputFolder(_item);
        Directory.CreateDirectory(outputFolder);

        string? referer = DownloadPathService.GetHeader(_item.CustomHeaders, "Referer");
        string? cookieHeader = DownloadPathService.GetHeader(_item.CustomHeaders, "Cookie");
        string fileStem = string.IsNullOrWhiteSpace(_item.FileName)
            ? DownloadPathService.SanitizeFileName(
                DownloadPathService.GuessFileName(_item.Url))
            : DownloadPathService.SanitizeFileName(
                Path.GetFileNameWithoutExtension(_item.FileName));

        _item.Status = DownloadStatus.Connecting;

        try
        {
            List<ResolvedStream> streams;
            try
            {
                streams = await YtDlpService.Instance.ResolveDirectUrlsAsync(
                    _item.Url,
                    _item.FormatId ?? "bestvideo+bestaudio/best",
                    referer,
                    cookieHeader,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Không resolve được URL từ yt-dlp: " + ex.Message,
                    ex);
            }

            if (streams.Count == 0)
            {
                SetError("yt-dlp không trả về stream nào để tải.");
                return;
            }

            _item.TotalBytes = streams.Sum(stream => stream.FilesizeApprox);
            _item.Status = DownloadStatus.Downloading;
            _item.StartTime = DateTime.Now;

            List<DownloadedStreamFile> rawFiles = await DownloadStreamsAsync(
                streams,
                tempDirectory,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            _item.Status = DownloadStatus.Merging;
            _reportProgress(_item.DownloadedBytes, 0);

            string finalPath = rawFiles.Count == 1
                ? MoveSingleStream(rawFiles[0], outputFolder, fileStem)
                : await _ffmpegMuxer.MuxAsync(
                    rawFiles,
                    outputFolder,
                    fileStem,
                    cancellationToken);

            Complete(finalPath, rawFiles);
            DownloadPathService.CleanupTemp(tempDirectory);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
    }

    private async Task<List<DownloadedStreamFile>> DownloadStreamsAsync(
        IReadOnlyList<ResolvedStream> streams,
        string tempDirectory,
        CancellationToken cancellationToken)
    {
        var files = new List<DownloadedStreamFile>(streams.Count);
        long progressBaseOffset = 0;

        foreach (ResolvedStream stream in streams)
        {
            string extension = string.IsNullOrWhiteSpace(stream.Ext) ? "bin" : stream.Ext;
            string kind = stream.HasVideo ? "video" : "audio";

            // Giữ nguyên cấu trúc temp của phiên bản cũ:
            //   video.<ext> + video_segs/
            //   audio.<ext> + audio_segs/
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

            DownloadProbeResult probe = await streamDownloader.ProbeAndDownloadAsync(
                stream.Url,
                rawPath,
                segmentDirectory,
                _item.Threads,
                progressBaseOffset,
                _reportProgress,
                progress => _reportThreadProgress(progressStage, progress),
                cancellationToken);

            long actualLength = File.Exists(rawPath)
                ? new FileInfo(rawPath).Length
                : probe.TotalBytes;
            progressBaseOffset += actualLength;
            files.Add(new DownloadedStreamFile(stream, rawPath));
            DownloadPathService.CleanupTemp(segmentDirectory);
        }

        if (_item.TotalBytes <= 0 || progressBaseOffset > _item.TotalBytes)
        {
            _item.TotalBytes = progressBaseOffset;
        }

        return files;
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

    private void Complete(
        string finalPath,
        IReadOnlyCollection<DownloadedStreamFile> rawFiles)
    {
        long sourceBytes = rawFiles.Sum(file =>
            File.Exists(file.Path) ? new FileInfo(file.Path).Length : file.Stream.FilesizeApprox);
        long finalLength = new FileInfo(finalPath).Length;

        _item.TotalBytes = Math.Max(_item.TotalBytes, Math.Max(sourceBytes, finalLength));
        _item.FileName = Path.GetFileName(finalPath);
        _item.SavePath = finalPath;
        _reportProgress(_item.TotalBytes, 0);
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
