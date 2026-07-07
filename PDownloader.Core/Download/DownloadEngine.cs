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

using System.Globalization;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace PDownloader.Core.Download;

public class DownloadEngine
{
    private const int MaxRetries = 5;
    private const int BufferSize = 81920;       // 80 KB read buffer
    private const string StateExt = ".pdstate";  // persisted segment state

    private const long MinSizeForMultiSegment = 5 * 1024 * 1024; // 5 MB

    private readonly DownloadItem _item;
    private readonly CancellationToken _ct;
    private static readonly HttpClient _defaultHttp = CreateHttpClient();
    private readonly HttpClient _http;

    public DownloadEngine(DownloadItem item, IProgress<DownloadProgress> progress, CancellationToken ct)
    {
        _item = item;
        _ct = ct;
        _http = BuildHttpClient(item.CustomHeaders);
    }

    public async Task RunAsync()
    {
        string tempDir = GetTempDir();
        Directory.CreateDirectory(tempDir);

        if (_item.IsYoutube)
        {
            if (await TryHandleHlsPlaylistAsync(tempDir))
            {
                return;
            }

            await RunYtDlpAsync(tempDir);
            return;
        }

        try
        {
            if (await TryHandleHlsPlaylistAsync(tempDir))
            {
                return;
            }

            (long totalBytes, bool supportsRange) = await ProbeAsync(_item.Url);
            _item.TotalBytes = totalBytes;

            bool useMultiSegment = supportsRange
                                   && totalBytes >= MinSizeForMultiSegment;

            int threadCount = useMultiSegment ? _item.Threads : 1;

            List<SegmentInfo> segments = BuildOrRestoreSegments(tempDir, totalBytes, threadCount);

            _item.Status = DownloadStatus.Downloading;
            _item.StartTime = DateTime.Now;

            using var speedTimer = new System.Timers.Timer(1000);
            long lastReported = segments.Sum(s => s.BytesWritten);
            speedTimer.Elapsed += (_, _) =>
            {
                long current = segments.Sum(s => s.BytesWritten);
                double speed = current - lastReported;
                lastReported = current;
                _item.DownloadedBytes = current;
                _item.SpeedBps = speed;
                PersistState(tempDir, segments);
            };
            speedTimer.Start();

            await DownloadAllSegmentsAsync(segments, supportsRange, _item.Url);

            speedTimer.Stop();

            _ct.ThrowIfCancellationRequested();

            var incomplete = segments.Where(s => !s.IsCompleted).ToList();
            if (incomplete.Count > 0)
            {
                string ids = string.Join(", ", incomplete.Select(s => s.Index));
                throw new InvalidOperationException(
                    $"Tải chưa hoàn tất: {incomplete.Count} segment chưa xong (index: {ids}).");
            }

            _item.Status = DownloadStatus.Merging;
            await MergeSegmentsAsync(segments);

            CleanupTemp(tempDir);

            _item.DownloadedBytes = _item.TotalBytes;
            _item.SpeedBps = 0;
            _item.Status = DownloadStatus.Completed;
            _item.EndTime = DateTime.Now;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    private async Task<bool> TryHandleHlsPlaylistAsync(string tempDir)
    {
        try
        {
            using var sniffReq = new HttpRequestMessage(HttpMethod.Get, _item.Url);
            sniffReq.Headers.Range = new RangeHeaderValue(0, 31);
            using HttpResponseMessage sniffResp = await _http.SendAsync(
                sniffReq, HttpCompletionOption.ResponseHeadersRead, _ct);

            if (!sniffResp.IsSuccessStatusCode)
            {
                return false;
            }

            byte[] head = await sniffResp.Content.ReadAsByteArrayAsync(_ct);
            string magic = Encoding.ASCII.GetString(head).TrimStart();

            if (!magic.StartsWith("#EXTM3U", StringComparison.Ordinal))
            {
                return false;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }

        if (YtDlpService.Instance.FindYtDlp() == null)
        {
            _item.Status = DownloadStatus.Error;
            _item.ErrorMessage =
                "Phát hiện đây là playlist HLS (m3u8) nhưng cần yt-dlp để tải/ghép. " +
                "Đặt yt-dlp.exe cạnh PDownloader.Core.exe hoặc thêm vào PATH.";
            return true;
        }

        string folder = string.IsNullOrWhiteSpace(_item.SavePath)
            ? CFSCommandHandler.DownloadConfigService.DownloadConfigs?.DefaultDownloadFolder ?? Helpers.GetDefaultFolder()
            : _item.SavePath;
        Directory.CreateDirectory(folder);

        string stem = string.IsNullOrWhiteSpace(_item.FileName)
            ? SanitizeFileName(GuessFileName(_item.Url))
            : SanitizeFileName(Path.GetFileNameWithoutExtension(_item.FileName));

        string? referer = _item.CustomHeaders?
            .FirstOrDefault(kv => kv.Key.Equals("Referer", StringComparison.OrdinalIgnoreCase)).Value;
        string? cookieHeader = _item.CustomHeaders?
            .FirstOrDefault(kv => kv.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase)).Value;

        _item.Status = DownloadStatus.Connecting;

        YtDlpService.HlsFragmentsResult? fragResult = null;
        try
        {
            fragResult = await YtDlpService.Instance.ResolveHlsFragmentsAsync(
                _item.Url, referer, cookieHeader, _ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            fragResult = null;
        }

        string finalPath;

        if (fragResult != null && fragResult.FragmentUrls.Count > 0)
        {
            finalPath = await DownloadHlsFragmentsInParallelAsync(fragResult, folder, stem, tempDir);
        }
        else
        {
            string uniqueWithExt = UniqueFilePath(folder, $"{stem}.mp4");
            string outputPathNoExt = Path.Combine(
                Path.GetDirectoryName(uniqueWithExt) ?? folder,
                Path.GetFileNameWithoutExtension(uniqueWithExt));

            long lastDownloaded = 0;
            long lastTotal = 0;

            _item.Status = DownloadStatus.Downloading;
            _item.StartTime = DateTime.Now;

            using var speedTimer = new System.Timers.Timer(1000);
            long lastReported = 0;
            speedTimer.Elapsed += (_, _) =>
            {
                long current = Interlocked.Read(ref lastDownloaded);
                double speed = current - lastReported;
                lastReported = current;
                _item.DownloadedBytes = current;
                _item.TotalBytes = Interlocked.Read(ref lastTotal);
                _item.SpeedBps = speed;
            };
            speedTimer.Start();

            try
            {
                finalPath = await DownloadHlsViaYtDlpProcessAsync(
                    _item.Url,
                    outputPathNoExt,
                    referer,
                    cookieHeader,
                    threadCount: _item.Threads,
                    onProgress: (downloaded, total) =>
                    {
                        Interlocked.Exchange(ref lastDownloaded, downloaded);
                        Interlocked.Exchange(ref lastTotal, total);
                    },
                    ct: _ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                speedTimer.Stop();
                _item.Status = DownloadStatus.Error;
                _item.ErrorMessage = "Không tải được HLS bằng yt-dlp: " + ex.Message;
                return true;
            }

            speedTimer.Stop();
        }

        CleanupTemp(tempDir);

        _item.FileName = Path.GetFileName(finalPath);
        _item.SavePath = finalPath;
        _item.TotalBytes = new FileInfo(finalPath).Length;
        _item.DownloadedBytes = _item.TotalBytes;
        _item.SpeedBps = 0;
        _item.Status = DownloadStatus.Completed;
        _item.EndTime = DateTime.Now;

        return true;
    }

    private async Task<string> DownloadHlsFragmentsInParallelAsync(
        YtDlpService.HlsFragmentsResult fragResult, string folder, string stem, string tempDir)
    {
        List<string> urls = fragResult.FragmentUrls;
        int total = urls.Count;
        var bytesPerFragment = new long[total];
        var tempPaths = new string[total];

        _item.Status = DownloadStatus.Downloading;
        _item.StartTime = DateTime.Now;
        _item.TotalBytes = 0;

        using var speedTimer = new System.Timers.Timer(1000);
        long lastReported = 0;
        speedTimer.Elapsed += (_, _) =>
        {
            long current = bytesPerFragment.Sum();
            double speed = current - lastReported;
            lastReported = current;
            _item.DownloadedBytes = current;
            _item.SpeedBps = speed;
        };
        speedTimer.Start();

        int concurrency = Math.Clamp(_item.Threads, 1, 16);
        using var semaphore = new SemaphoreSlim(concurrency);

        var tasks = new Task[total];
        for (int i = 0; i < total; i++)
        {
            int idx = i;
            tasks[idx] = Task.Run(async () =>
            {
                await semaphore.WaitAsync(_ct);
                try
                {
                    string tempPath = Path.Combine(tempDir, $"frag_{idx:D5}.part");
                    tempPaths[idx] = tempPath;
                    await DownloadOneFragmentWithRetryAsync(urls[idx], tempPath, idx, bytesPerFragment);
                }
                finally
                {
                    semaphore.Release();
                }
            }, _ct);
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        finally
        {
            speedTimer.Stop();
        }

        string ext = string.IsNullOrWhiteSpace(fragResult.Ext) ? "ts" : fragResult.Ext;
        string finalPath = UniqueFilePath(folder, $"{stem}.{ext}");
        string? dir = Path.GetDirectoryName(finalPath);
        if (dir != null)
        {
            Directory.CreateDirectory(dir);
        }

        _item.Status = DownloadStatus.Merging;

        using (var outStream = new FileStream(finalPath, FileMode.Create, FileAccess.Write))
        {
            for (int i = 0; i < total; i++)
            {
                _ct.ThrowIfCancellationRequested();
                using var inStream = new FileStream(tempPaths[i], FileMode.Open, FileAccess.Read);
                await inStream.CopyToAsync(outStream, _ct);
            }
        }

        foreach (string p in tempPaths)
        {
            try { File.Delete(p); } catch { /* best effort */ }
        }

        return finalPath;
    }

    private async Task DownloadOneFragmentWithRetryAsync(
        string url, string tempPath, int idx, long[] bytesPerFragment, int maxRetries = 3)
    {
        Exception? last = null;

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            _ct.ThrowIfCancellationRequested();
            Interlocked.Exchange(ref bytesPerFragment[idx], 0);

            try
            {
                using HttpResponseMessage resp = await _http.SendAsync(
                    new HttpRequestMessage(HttpMethod.Get, url),
                    HttpCompletionOption.ResponseHeadersRead, _ct);
                resp.EnsureSuccessStatusCode();

                await using (var outFs = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                await using (Stream inStream = await resp.Content.ReadAsStreamAsync(_ct))
                {
                    byte[] buffer = new byte[BufferSize];
                    int read;
                    while ((read = await inStream.ReadAsync(buffer, _ct)) > 0)
                    {
                        await outFs.WriteAsync(buffer.AsMemory(0, read), _ct);
                        Interlocked.Add(ref bytesPerFragment[idx], read);
                    }
                }

                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(300 * (attempt + 1), _ct);
            }
        }

        throw new InvalidOperationException(
            $"Tải fragment HLS thất bại sau {maxRetries} lần thử: {url}", last);
    }

    private async Task<string> DownloadHlsViaYtDlpProcessAsync(
        string url,
        string outputPathNoExt,
        string? referer,
        string? cookieHeader,
        int threadCount,
        Action<long, long>? onProgress,
        CancellationToken ct)
    {
        var bin = YtDlpService.Instance.FindYtDlp()
            ?? throw new InvalidOperationException("yt-dlp không tìm thấy.");

        int fragments = Math.Clamp(threadCount, 1, 16);

        string refererArg = string.IsNullOrWhiteSpace(referer)
            ? ""
            : $"--add-header \"Referer:{EscapeArg(referer)}\" ";

        string? cookieFile = YtDlpService.WriteNetscapeCookieFile(cookieHeader);
        string cookieArg = cookieFile == null
            ? ""
            : $"--cookies \"{EscapeArg(cookieFile)}\" ";

        string outputTemplate = $"{EscapeArg(outputPathNoExt)}.%(ext)s";

        string args = "--newline --no-warnings --no-playlist " +
                      "--merge-output-format mp4 " +
                      "--hls-prefer-native " +
                      $"--concurrent-fragments {fragments} " +
                      refererArg + cookieArg +
                      $"-o \"{outputTemplate}\" " +
                      $"-- \"{EscapeArg(url)}\"";

        try
        {
            (string? finalPath, string? stderr, int exitCode) = await RunYtDlpWithProgressAsync(
                bin, args, onProgress, ct);

            if (exitCode != 0)
            {
                throw new InvalidOperationException(YtDlpService.ParseYtDlpError(stderr ?? ""));
            }

            if (finalPath != null && File.Exists(finalPath))
            {
                return finalPath;
            }

            string? dir = Path.GetDirectoryName(outputPathNoExt);
            string stem = Path.GetFileName(outputPathNoExt);
            string? found = !string.IsNullOrEmpty(dir) && Directory.Exists(dir)
                ? Directory.GetFiles(dir, stem + ".*").FirstOrDefault()
                : null;

            if (found == null)
            {
                throw new InvalidOperationException(
                    "yt-dlp báo thành công nhưng không tìm thấy file kết quả.");
            }

            return found;
        }
        finally
        {
            YtDlpService.DeleteCookieFileSafe(cookieFile);
        }
    }

    private static readonly Regex HlsProgressRegex = new(
        @"^\[download\]\s+(?<pct>[\d.]+)%\s+of\s+~?\s*(?<size>[\d.]+)(?<unit>Ki?B|Mi?B|Gi?B|B)",
        RegexOptions.Compiled);

    private static readonly Regex HlsDestinationRegex = new(
        @"^\[(?:download|Merger)\]\s+(?:Destination:|Merging formats into)\s*""?(?<path>.+?)""?$",
        RegexOptions.Compiled);

    private static async Task<(string? finalPath, string? stderr, int exitCode)> RunYtDlpWithProgressAsync(
        string bin, string args,
        Action<long, long>? onProgress,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = bin,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var proc = new Process { StartInfo = psi };

        var stderrSb = new StringBuilder();
        string? finalPath = null;

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null)
            {
                return;
            }

            Match pm = HlsProgressRegex.Match(e.Data);
            if (pm.Success)
            {
                double pct = double.Parse(pm.Groups["pct"].Value, CultureInfo.InvariantCulture);
                double size = double.Parse(pm.Groups["size"].Value, CultureInfo.InvariantCulture);
                long totalBytes = (long)(size * HlsUnitMultiplier(pm.Groups["unit"].Value));
                long downloadedBytes = (long)(totalBytes * pct / 100.0);
                onProgress?.Invoke(downloadedBytes, totalBytes);
                return;
            }

            Match dm = HlsDestinationRegex.Match(e.Data);
            if (dm.Success)
            {
                finalPath = dm.Groups["path"].Value.Trim();
            }
        };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) { stderrSb.AppendLine(e.Data); } };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try
        {
            await proc.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }

            throw;
        }

        return (finalPath, stderrSb.ToString(), proc.ExitCode);
    }

    private static double HlsUnitMultiplier(string unit) => unit switch
    {
        "B" => 1,
        "KiB" => 1024,
        "MiB" => 1024 * 1024,
        "GiB" => 1024 * 1024 * 1024,
        "KB" => 1000,
        "MB" => 1000 * 1000,
        "GB" => 1000 * 1000 * 1000,
        _ => 1,
    };

    private async Task RunYtDlpAsync(string tempDir)
    {
        if (YtDlpService.Instance.FindYtDlp() == null)
        {
            _item.Status = DownloadStatus.Error;
            _item.ErrorMessage = "yt-dlp không tìm thấy.";
            return;
        }

        string folder = string.IsNullOrWhiteSpace(_item.SavePath)
            ? CFSCommandHandler.DownloadConfigService.DownloadConfigs?.DefaultDownloadFolder ?? Helpers.GetDefaultFolder()
            : _item.SavePath;

        Directory.CreateDirectory(folder);

        string? referer = _item.CustomHeaders?
            .FirstOrDefault(kv => kv.Key.Equals("Referer", StringComparison.OrdinalIgnoreCase)).Value;

        string? cookieHeader = _item.CustomHeaders?
            .FirstOrDefault(kv => kv.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase)).Value;

        string stem = string.IsNullOrWhiteSpace(_item.FileName)
            ? SanitizeFileName(GuessFileName(_item.Url))
            : SanitizeFileName(Path.GetFileNameWithoutExtension(_item.FileName));

        _item.Status = DownloadStatus.Connecting;

        List<YtDlpService.ResolvedStream> streams;
        try
        {
            streams = await YtDlpService.Instance.ResolveDirectUrlsAsync(
                _item.Url, _item.FormatId ?? "bestvideo+bestaudio/best", referer,
                cookieHeader: cookieHeader, ct: _ct);
        }
        catch (Exception ex)
        {
            _item.Status = DownloadStatus.Error;
            _item.ErrorMessage = "Không resolve được URL từ yt-dlp: " + ex.Message;
            return;
        }

        if (streams.Count == 0)
        {
            _item.Status = DownloadStatus.Error;
            _item.ErrorMessage = "yt-dlp không trả về stream nào để tải.";
            return;
        }

        _item.TotalBytes = streams.Sum(s => s.FilesizeApprox);
        _item.Status = DownloadStatus.Downloading;
        _item.StartTime = DateTime.Now;

        var rawFiles = new List<(YtDlpService.ResolvedStream stream, string path)>();

        try
        {
            long progressBase = 0;
            foreach (YtDlpService.ResolvedStream stream in streams)
            {
                string ext = string.IsNullOrWhiteSpace(stream.Ext) ? "bin" : stream.Ext;
                string rawPath = Path.Combine(
                    tempDir, (stream.HasVideo ? "video" : "audio") + "." + ext);

                await DownloadUrlMultiSegmentAsync(stream.Url, rawPath, tempDir, progressBase, _ct);

                progressBase += File.Exists(rawPath) ? new FileInfo(rawPath).Length : 0;
                rawFiles.Add((stream, rawPath));
            }

            _ct.ThrowIfCancellationRequested();
            _item.Status = DownloadStatus.Merging;

            string finalPath;
            if (rawFiles.Count == 1)
            {
                string ext = string.IsNullOrWhiteSpace(rawFiles[0].stream.Ext) ? "mp4" : rawFiles[0].stream.Ext;
                finalPath = UniqueFilePath(folder, $"{stem}.{ext}");
                string? dir = Path.GetDirectoryName(finalPath);
                if (dir != null)
                {
                    Directory.CreateDirectory(dir);
                }

                File.Move(rawFiles[0].path, finalPath, overwrite: true);
            }
            else
            {
                finalPath = await MuxStreamsWithFfmpegAsync(rawFiles, folder, stem, _ct);
            }

            CleanupTemp(tempDir);

            _item.FileName = Path.GetFileName(finalPath);
            _item.SavePath = finalPath;
            _item.DownloadedBytes = _item.TotalBytes;
            _item.SpeedBps = 0;
            _item.Status = DownloadStatus.Completed;
            _item.EndTime = DateTime.Now;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _item.Status = DownloadStatus.Error;
            _item.ErrorMessage = ex.Message;
        }
    }

    private async Task<string> MuxStreamsWithFfmpegAsync(
        List<(YtDlpService.ResolvedStream stream, string path)> rawFiles,
        string folder, string stem, CancellationToken ct)
    {
        var ffmpeg = YtDlpService.Instance.FindFfmpeg();
        if (ffmpeg == null)
        {
            throw new InvalidOperationException(
                "ffmpeg không tìm thấy — cần ffmpeg để ghép video+audio tải riêng thành 1 file. " +
                "Đặt ffmpeg.exe cạnh PDownloader.Core.exe hoặc thêm vào PATH.");
        }

        (YtDlpService.ResolvedStream stream, string path) video = rawFiles.FirstOrDefault(r => r.stream.HasVideo);
        (YtDlpService.ResolvedStream stream, string path) audio = rawFiles.FirstOrDefault(r => r.stream.HasAudio && !r.stream.HasVideo);

        if (video.path == null)
        {
            video = rawFiles[0];
        }

        string videoExt = (video.stream?.Ext ?? "mp4").ToLowerInvariant();
        string outExt = videoExt is "mp4" or "webm" or "mkv" ? videoExt : "mkv";

        string finalPath = UniqueFilePath(folder, $"{stem}.{outExt}");
        string? dir = Path.GetDirectoryName(finalPath);
        if (dir != null)
        {
            Directory.CreateDirectory(dir);
        }

        string args = $"-y -i \"{video.path}\" " +
                      (audio.path != null ? $"-i \"{audio.path}\" " : "") +
                      "-c copy " +
                      $"\"{finalPath}\"";

        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var proc = new Process { StartInfo = psi };
        proc.Start();
        string stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0 || !File.Exists(finalPath))
        {
            string tail = stderr.Length > 500 ? stderr[^500..] : stderr;
            throw new InvalidOperationException($"ffmpeg ghép thất bại (exit {proc.ExitCode}): {tail}");
        }

        foreach ((YtDlpService.ResolvedStream stream, string path) r in rawFiles)
        {
            try { File.Delete(r.path); } catch { }
        }

        return finalPath;
    }

    private async Task<(long totalBytes, bool supportsRange)> ProbeAsync(string url, bool assignItemFileName = true)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            using HttpResponseMessage resp = await _http.SendAsync(req, _ct);

            bool headUnreliable = !resp.IsSuccessStatusCode
                                   || resp.Content.Headers.ContentLength is null or 0;

            if (headUnreliable)
            {
                return await ProbeViaRangedGetAsync(url, assignItemFileName);
            }

            long total = resp.Content.Headers.ContentLength ?? 0;
            bool ranges = resp.Headers.AcceptRanges.Contains("bytes");

            if (assignItemFileName && string.IsNullOrWhiteSpace(_item.FileName))
            {
                ContentDispositionHeaderValue? cd = resp.Content.Headers.ContentDisposition;
                _item.FileName = cd?.FileNameStar ?? cd?.FileName
                    ?? GuessFileName(resp.RequestMessage?.RequestUri?.ToString() ?? _item.Url);
                _item.FileName = SanitizeFileName(_item.FileName);
            }

            return (total, ranges);
        }
        catch
        {
            try { return await ProbeViaRangedGetAsync(url, assignItemFileName); }
            catch
            {
                if (assignItemFileName && string.IsNullOrWhiteSpace(_item.FileName))
                {
                    _item.FileName = SanitizeFileName(GuessFileName(_item.Url));
                }

                return (0, false);
            }
        }
    }

    private async Task<(long totalBytes, bool supportsRange)> ProbeViaRangedGetAsync(string url, bool assignItemFileName = true)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Range = new RangeHeaderValue(0, 0);

        using HttpResponseMessage resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, _ct);

        bool supportsRange = resp.StatusCode == System.Net.HttpStatusCode.PartialContent;
        long total = resp.Content.Headers.ContentRange?.Length
                     ?? resp.Content.Headers.ContentLength
                     ?? 0;

        if (assignItemFileName && string.IsNullOrWhiteSpace(_item.FileName))
        {
            ContentDispositionHeaderValue? cd = resp.Content.Headers.ContentDisposition;
            _item.FileName = cd?.FileNameStar ?? cd?.FileName
                ?? GuessFileName(resp.RequestMessage?.RequestUri?.ToString() ?? _item.Url);
            _item.FileName = SanitizeFileName(_item.FileName);
        }

        return (total, supportsRange);
    }

    private List<SegmentInfo> BuildOrRestoreSegments(string tempDir, long totalBytes, int threadCount)
    {
        string stateFile = Path.Combine(tempDir, "segments" + StateExt);

        if (File.Exists(stateFile))
        {
            try
            {
                List<SegmentInfo>? saved = JsonSerializer.Deserialize<List<SegmentInfo>>(File.ReadAllText(stateFile));
                if (saved != null && saved.Count == threadCount)
                {
                    foreach (SegmentInfo seg in saved)
                    {
                        long actualLen = File.Exists(seg.TempFilePath)
                            ? new FileInfo(seg.TempFilePath).Length
                            : 0;

                        if (actualLen != seg.BytesWritten)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"[Engine] Segment {seg.Index}: state={seg.BytesWritten}B " +
                                $"thực tế={actualLen}B — đồng bộ theo file.");
                            seg.BytesWritten = actualLen;
                        }
                    }

                    return saved;
                }
            }
            catch { }
        }

        var segments = new List<SegmentInfo>();

        if (threadCount == 1 || totalBytes <= 0)
        {
            segments.Add(new SegmentInfo
            {
                Index = 0,
                RangeStart = 0,
                RangeEnd = totalBytes > 0 ? totalBytes - 1 : -1,
                TempFilePath = Path.Combine(tempDir, "seg_0.part"),
                BytesWritten = 0
            });
        }
        else
        {
            long chunkSize = totalBytes / threadCount;
            for (int i = 0; i < threadCount; i++)
            {
                long start = i * chunkSize;
                long end = i == threadCount - 1 ? totalBytes - 1 : start + chunkSize - 1;
                segments.Add(new SegmentInfo
                {
                    Index = i,
                    RangeStart = start,
                    RangeEnd = end,
                    TempFilePath = Path.Combine(tempDir, $"seg_{i}.part"),
                    BytesWritten = 0
                });
            }
        }

        return segments;
    }

    private void PersistState(string tempDir, List<SegmentInfo> segments)
    {
        try
        {
            string stateFile = Path.Combine(tempDir, "segments" + StateExt);
            File.WriteAllText(stateFile, JsonSerializer.Serialize(segments));
        }
        catch { }
    }

    private async Task DownloadAllSegmentsAsync(List<SegmentInfo> segments, bool supportsRange, string url)
    {
        IEnumerable<Task> tasks = segments
            .Where(s => !s.IsCompleted)
            .Select(seg => DownloadSegmentWithRetryAsync(seg, supportsRange, url));

        await Task.WhenAll(tasks);
    }

    private const int StallTimeoutSeconds = 20;

    private async Task DownloadSegmentWithRetryAsync(SegmentInfo seg, bool supportsRange, string url)
    {
        int attempt = 0;
        bool rangeDisabledForThisSegment = false;
        while (true)
        {
            _ct.ThrowIfCancellationRequested();
            try
            {
                await DownloadSegmentAsync(seg, supportsRange && !rangeDisabledForThisSegment, url);
                return;
            }
            catch (OperationCanceledException) when (_ct.IsCancellationRequested)
            {
                throw;
            }
            catch (RangeRejectedException) when (!rangeDisabledForThisSegment)
            {
                rangeDisabledForThisSegment = true;
                System.Diagnostics.Debug.WriteLine(
                    $"[Engine] Segment {seg.Index}: server từ chối Range (403). Thử lại không Range.");
            }
            catch (Exception ex) when (attempt < MaxRetries)
            {
                attempt++;
                int delay = (int)Math.Pow(2, attempt) * 500;
                System.Diagnostics.Debug.WriteLine(
                    $"[Engine] Segment {seg.Index} attempt {attempt} failed: {ex.Message}. Retry in {delay}ms");
                await Task.Delay(delay, _ct);
            }
        }
    }

    private async Task DownloadSegmentAsync(SegmentInfo seg, bool supportsRange, string url)
    {
        long actualLen = File.Exists(seg.TempFilePath)
            ? new FileInfo(seg.TempFilePath).Length
            : 0;
        if (actualLen != seg.BytesWritten)
        {
            seg.BytesWritten = actualLen;
        }

        using var req = new HttpRequestMessage(HttpMethod.Get, url);

        long resumeFrom = seg.RangeStart + seg.BytesWritten;

        bool shouldSetRange = supportsRange
                              && seg.RangeEnd >= 0
                              && (seg.RangeStart > 0 || seg.BytesWritten > 0 || seg.RangeEnd < long.MaxValue - 1);

        if (shouldSetRange)
        {
            req.Headers.Range = new RangeHeaderValue(resumeFrom, seg.RangeEnd);
        }

        using var stallCts = CancellationTokenSource.CreateLinkedTokenSource(_ct);
        stallCts.CancelAfter(TimeSpan.FromSeconds(StallTimeoutSeconds));

        using HttpResponseMessage resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, stallCts.Token);
        stallCts.CancelAfter(TimeSpan.FromSeconds(StallTimeoutSeconds));

        if (resp.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            long expectedBytes = seg.RangeEnd >= 0
                ? seg.RangeEnd - seg.RangeStart + 1
                : -1;

            if (expectedBytes > 0 && seg.BytesWritten >= expectedBytes)
            {
                seg.IsCompleted = true;
                return;
            }

            throw new HttpRequestException($"Server trả 416 cho segment {seg.Index} " +
                $"(range {resumeFrom}-{seg.RangeEnd}), đã có {seg.BytesWritten}B.");
        }

        if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden && shouldSetRange)
        {
            throw new RangeRejectedException(
                $"Server trả 403 khi request có header Range cho segment {seg.Index}.");
        }

        resp.EnsureSuccessStatusCode();

        var contentType = resp.Content.Headers.ContentType?.MediaType ?? "";
        if (IsHtmlContentType(contentType))
        {
            throw new InvalidOperationException(
                "Server trả về trang HTML thay vì file. " +
                "Có thể URL yêu cầu đăng nhập hoặc đã hết hạn.");
        }

        bool serverHonoredRange = resp.StatusCode == System.Net.HttpStatusCode.PartialContent;
        FileMode fileMode = (seg.BytesWritten > 0 && serverHonoredRange)
            ? FileMode.Append
            : FileMode.Create;

        if (fileMode == FileMode.Create)
        {
            seg.BytesWritten = 0;
        }

        await using var fs = new FileStream(seg.TempFilePath, fileMode, FileAccess.Write, FileShare.None);
        await using Stream stream = await resp.Content.ReadAsStreamAsync(_ct);

        byte[] buffer = new byte[BufferSize];
        int firstRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), stallCts.Token);
        stallCts.CancelAfter(TimeSpan.FromSeconds(StallTimeoutSeconds));

        if (firstRead > 0 && seg.BytesWritten == 0 && seg.Index == 0)
        {
            if (LooksLikeHtml(buffer, firstRead))
            {
                throw new InvalidOperationException(
                    "Nội dung tải về là trang HTML (trang lỗi hoặc yêu cầu đăng nhập), không phải file thật.");
            }
        }

        if (firstRead > 0)
        {
            _ct.ThrowIfCancellationRequested();
            await fs.WriteAsync(buffer.AsMemory(0, firstRead), _ct);
            seg.BytesWritten += firstRead;
        }

        int read;
        while ((read = await stream.ReadAsync(buffer, stallCts.Token)) > 0)
        {
            stallCts.CancelAfter(TimeSpan.FromSeconds(StallTimeoutSeconds));
            _ct.ThrowIfCancellationRequested();
            await fs.WriteAsync(buffer.AsMemory(0, read), _ct);
            seg.BytesWritten += read;
        }

        seg.IsCompleted = true;
    }

    private async Task MergeSegmentsAsync(List<SegmentInfo> segments)
    {
        var missing = segments.Where(s => !File.Exists(s.TempFilePath)).ToList();
        if (missing.Count > 0)
        {
            string ids = string.Join(", ", missing.Select(s => s.Index));
            throw new InvalidOperationException(
                $"Không thể ghép file: thiếu {missing.Count} segment (index: {ids}).");
        }

        string finalPath = GetFinalPath();
        await MergeSegmentsToRawFileAsync(segments, finalPath, _ct);
        _item.SavePath = finalPath;
    }

    private async Task MergeSegmentsToRawFileAsync(List<SegmentInfo> segments, string destPath, CancellationToken ct)
    {
        string? dir = Path.GetDirectoryName(destPath);
        if (dir != null)
        {
            Directory.CreateDirectory(dir);
        }

        string mergingPath = destPath + ".merging";

        await using (var output = new FileStream(mergingPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            foreach (SegmentInfo? seg in segments.OrderBy(s => s.Index))
            {
                await using (var input = new FileStream(seg.TempFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    await input.CopyToAsync(output, ct);
                }

                try { File.Delete(seg.TempFilePath); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Engine] Không thể xóa segment {seg.Index} ngay sau khi ghép: {ex.Message}");
                }
            }
        }

        File.Move(mergingPath, destPath, overwrite: true);
    }

    private async Task DownloadUrlMultiSegmentAsync(
        string url, string destPath, string tempDirRoot, long progressBaseOffset, CancellationToken ct)
    {
        string subTempDir = Path.Combine(tempDirRoot, Path.GetFileNameWithoutExtension(destPath) + "_segs");
        Directory.CreateDirectory(subTempDir);

        (long totalBytes, bool supportsRange) = await ProbeAsync(url, assignItemFileName: false);

        bool useMultiSegment = supportsRange && totalBytes >= MinSizeForMultiSegment;
        int threadCount = useMultiSegment ? _item.Threads : 1;

        List<SegmentInfo> segments = BuildOrRestoreSegments(subTempDir, totalBytes, threadCount);

        using var speedTimer = new System.Timers.Timer(1000);
        long lastReported = segments.Sum(s => s.BytesWritten);
        speedTimer.Elapsed += (_, _) =>
        {
            long current = segments.Sum(s => s.BytesWritten);
            double speed = current - lastReported;
            lastReported = current;
            _item.DownloadedBytes = progressBaseOffset + current;
            _item.SpeedBps = speed;
            PersistState(subTempDir, segments);
        };
        speedTimer.Start();

        try
        {
            await DownloadAllSegmentsAsync(segments, supportsRange, url);
        }
        finally
        {
            speedTimer.Stop();
        }

        ct.ThrowIfCancellationRequested();

        var incomplete = segments.Where(s => !s.IsCompleted).ToList();
        if (incomplete.Count > 0)
        {
            string ids = string.Join(", ", incomplete.Select(s => s.Index));
            throw new InvalidOperationException(
                $"Tải chưa hoàn tất: {incomplete.Count} segment chưa xong (index: {ids}).");
        }

        await MergeSegmentsToRawFileAsync(segments, destPath, ct);

        CleanupTemp(subTempDir);
    }

    private static void CleanupTemp(string tempDir)
    {
        const int maxAttempts = 5;
        const int delayMs = 100;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }

                break;
            }
            catch (IOException) when (attempt < maxAttempts) { Thread.Sleep(delayMs); }
            catch (UnauthorizedAccessException) when (attempt < maxAttempts) { Thread.Sleep(delayMs); }
            catch
            {
                System.Diagnostics.Debug.WriteLine($"[Engine] Không thể xóa thư mục temp: {tempDir}");
                break;
            }
        }
    }

    private string GetTempDir() => GetTempDirFor(_item.Id);

    private static string GetTempDirFor(string id) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SM SOFT", "PDownloader", "Temp", id);

    public static void DeleteTempFiles(string id, string? savePath, string? fileName)
    {
        string tempDir = GetTempDirFor(id);
        CleanupTemp(tempDir);

        try
        {
            string folder = string.IsNullOrWhiteSpace(savePath)
                ? CFSCommandHandler.DownloadConfigService.DownloadConfigs?.DefaultDownloadFolder ?? Helpers.GetDefaultFolder()
                : savePath;
            string name = string.IsNullOrWhiteSpace(fileName) ? "download" : fileName;
            string mergingPath = Path.Combine(folder, name) + ".merging";
            if (File.Exists(mergingPath))
            {
                File.Delete(mergingPath);
            }
        }
        catch { }
    }

    private string GetFinalPath()
    {
        string folder = string.IsNullOrWhiteSpace(_item.SavePath)
            ? CFSCommandHandler.DownloadConfigService.DownloadConfigs?.DefaultDownloadFolder ?? Helpers.GetDefaultFolder()
            : _item.SavePath;
        string name = string.IsNullOrWhiteSpace(_item.FileName)
            ? "download"
            : _item.FileName;
        return UniqueFilePath(folder, name);
    }

    private static string EscapeArg(string s) => s.Replace("\"", "\\\"");

    private static string UniqueFilePath(string folder, string name)
    {
        string path = Path.Combine(folder, name);
        if (!File.Exists(path))
        {
            return path;
        }

        string noExt = Path.GetFileNameWithoutExtension(name);
        string ext = Path.GetExtension(name);
        int counter = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(folder, $"{noExt} ({counter}){ext}");
            counter++;
        }

        return path;
    }

    private static string GuessFileName(string url)
    {
        try
        {
            var uri = new Uri(url);
            string p = uri.AbsolutePath;
            string f = Path.GetFileName(p);
            return string.IsNullOrWhiteSpace(f) ? "download" : f;
        }
        catch { return "download"; }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        name = name.Trim('"', '\'', ' ');
        return string.IsNullOrWhiteSpace(name) ? "download" : name;
    }

    private static bool IsHtmlContentType(string mediaType)
    {
        if (string.IsNullOrEmpty(mediaType))
        {
            return false;
        }

        var t = mediaType.Trim().ToLowerInvariant();
        return t is "text/html" or "application/xhtml+xml" or "text/xhtml";
    }

    private static bool LooksLikeHtml(byte[] buffer, int length)
    {
        if (length < 5)
        {
            return false;
        }

        int offset = 0;
        if (length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
        {
            offset = 3;
        }
        else if (length >= 2 && ((buffer[0] == 0xFF && buffer[1] == 0xFE) ||
                                  (buffer[0] == 0xFE && buffer[1] == 0xFF)))
        {
            offset = 2;
        }

        int checkLen = Math.Min(length - offset, 100);
        if (checkLen < 5)
        {
            return false;
        }

        var span = System.Text.Encoding.UTF8.GetString(buffer, offset, checkLen)
                       .TrimStart()
                       .ToLowerInvariant();

        return span.StartsWith("<!doctype html", StringComparison.Ordinal)
            || span.StartsWith("<html", StringComparison.Ordinal)
            || span.StartsWith("<!doctype htm", StringComparison.Ordinal);
    }

    private static HttpClient BuildHttpClient(Dictionary<string, string>? customHeaders)
    {
        if (customHeaders == null || customHeaders.Count == 0)
        {
            return _defaultHttp;
        }

        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            UseCookies = false
        };
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");

        foreach ((string? key, string? value) in customHeaders)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            try
            {
                switch (key.ToLowerInvariant())
                {
                    case "cookie":
                        client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", value);
                        break;
                    case "referer":
                        client.DefaultRequestHeaders.Referrer = new Uri(value, UriKind.RelativeOrAbsolute);
                        break;
                    case "user-agent":
                        client.DefaultRequestHeaders.UserAgent.Clear();
                        client.DefaultRequestHeaders.UserAgent.ParseAdd(value);
                        break;
                    default:
                        client.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Engine] Bỏ qua header '{key}': {ex.Message}");
            }
        }

        return client;
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            UseCookies = true
        };
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
        return client;
    }
}

public record DownloadProgress(long DownloadedBytes, double SpeedBps);

public sealed class RangeRejectedException : Exception
{
    public RangeRejectedException(string message) : base(message) { }
}