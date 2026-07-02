using System.Net.Http.Headers;

namespace PDownloader.Core.Download
{
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
            _item     = item;
            _ct       = ct;
            _http     = BuildHttpClient(item.CustomHeaders);
        }

        public async Task RunAsync()
        {
            if (_item.IsYoutube)
            {
                await RunYtDlpAsync();
                return;
            }

            string tempDir = GetTempDir();
            Directory.CreateDirectory(tempDir);

            try
            {
                var (totalBytes, supportsRange) = await ProbeAsync(_item.Url);
                _item.TotalBytes = totalBytes;

                bool useMultiSegment = supportsRange
                                       && totalBytes >= MinSizeForMultiSegment;

                int threadCount = useMultiSegment ? _item.Threads : 1;

                var segments = BuildOrRestoreSegments(tempDir, totalBytes, threadCount);

                _item.Status    = DownloadStatus.Downloading;
                _item.StartTime = DateTime.Now;

                using var speedTimer = new System.Timers.Timer(1000);
                long lastReported = segments.Sum(s => s.BytesWritten);
                speedTimer.Elapsed += (_, _) =>
                {
                    long current = segments.Sum(s => s.BytesWritten);
                    double speed = current - lastReported;
                    lastReported = current;
                    _item.DownloadedBytes = current;
                    _item.SpeedBps        = speed;
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
                _item.SpeedBps        = 0;
                _item.Status          = DownloadStatus.Completed;
                _item.EndTime         = DateTime.Now;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
        }

        private async Task RunYtDlpAsync()
        {
            if (YtDlpService.Instance.FindYtDlp() == null)
            {
                _item.Status       = DownloadStatus.Error;
                _item.ErrorMessage = "yt-dlp không tìm thấy.";
                return;
            }

            string folder = string.IsNullOrWhiteSpace(_item.SavePath)
                ? CFSCommandHandler.DownloadConfigService.DownloadConfigs?.DefaultDownloadFolder ?? Helpers.GetDefaultFolder()
                : _item.SavePath;

            Directory.CreateDirectory(folder);

            string? referer = _item.CustomHeaders?
                .FirstOrDefault(kv => kv.Key.Equals("Referer", StringComparison.OrdinalIgnoreCase)).Value;

            string stem = string.IsNullOrWhiteSpace(_item.FileName)
                ? SanitizeFileName(GuessFileName(_item.Url))
                : SanitizeFileName(Path.GetFileNameWithoutExtension(_item.FileName));

            _item.Status = DownloadStatus.Connecting;

            List<YtDlpService.ResolvedStream> streams;
            try
            {
                streams = await YtDlpService.Instance.ResolveDirectUrlsAsync(
                    _item.Url, _item.FormatId ?? "bestvideo+bestaudio/best", referer, _ct);
            }
            catch (Exception ex)
            {
                _item.Status       = DownloadStatus.Error;
                _item.ErrorMessage = "Không resolve được URL từ yt-dlp: " + ex.Message;
                return;
            }

            if (streams.Count == 0)
            {
                _item.Status       = DownloadStatus.Error;
                _item.ErrorMessage = "yt-dlp không trả về stream nào để tải.";
                return;
            }

            string tempDir = GetTempDir();
            Directory.CreateDirectory(tempDir);

            _item.TotalBytes = streams.Sum(s => s.FilesizeApprox);
            _item.Status     = DownloadStatus.Downloading;
            _item.StartTime  = DateTime.Now;

            var rawFiles = new List<(YtDlpService.ResolvedStream stream, string path)>();

            try
            {
                long progressBase = 0;
                foreach (var stream in streams)
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
                    if (dir != null) Directory.CreateDirectory(dir);
                    File.Move(rawFiles[0].path, finalPath, overwrite: true);
                }
                else
                {
                    finalPath = await MuxStreamsWithFfmpegAsync(rawFiles, folder, stem, _ct);
                }

                CleanupTemp(tempDir);

                _item.FileName        = Path.GetFileName(finalPath);
                _item.SavePath        = finalPath;
                _item.DownloadedBytes = _item.TotalBytes;
                _item.SpeedBps        = 0;
                _item.Status          = DownloadStatus.Completed;
                _item.EndTime         = DateTime.Now;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _item.Status       = DownloadStatus.Error;
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

            var video = rawFiles.FirstOrDefault(r => r.stream.HasVideo);
            var audio = rawFiles.FirstOrDefault(r => r.stream.HasAudio && !r.stream.HasVideo);

            if (video.path == null) video = rawFiles[0];

            string videoExt = (video.stream?.Ext ?? "mp4").ToLowerInvariant();
            string outExt = videoExt is "mp4" or "webm" or "mkv" ? videoExt : "mkv";

            string finalPath = UniqueFilePath(folder, $"{stem}.{outExt}");
            string? dir = Path.GetDirectoryName(finalPath);
            if (dir != null) Directory.CreateDirectory(dir);

            string args = $"-y -i \"{video.path}\" " +
                          (audio.path != null ? $"-i \"{audio.path}\" " : "") +
                          "-c copy " +
                          $"\"{finalPath}\"";

            var psi = new ProcessStartInfo
            {
                FileName               = ffmpeg,
                Arguments              = args,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
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

            foreach (var r in rawFiles)
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
                using var resp = await _http.SendAsync(req, _ct);

                bool headUnreliable = !resp.IsSuccessStatusCode
                                       || resp.Content.Headers.ContentLength is null or 0;

                if (headUnreliable)
                    return await ProbeViaRangedGetAsync(url, assignItemFileName);

                long total = resp.Content.Headers.ContentLength ?? 0;
                bool ranges = resp.Headers.AcceptRanges.Contains("bytes");

                if (assignItemFileName && string.IsNullOrWhiteSpace(_item.FileName))
                {
                    var cd = resp.Content.Headers.ContentDisposition;
                    _item.FileName = cd?.FileNameStar ?? cd?.FileName ?? GuessFileName(_item.Url);
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
                        _item.FileName = SanitizeFileName(GuessFileName(_item.Url));
                    return (0, false);
                }
            }
        }

        private async Task<(long totalBytes, bool supportsRange)> ProbeViaRangedGetAsync(string url, bool assignItemFileName = true)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Range = new RangeHeaderValue(0, 0);

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, _ct);

            bool supportsRange = resp.StatusCode == System.Net.HttpStatusCode.PartialContent;
            long total = resp.Content.Headers.ContentRange?.Length
                         ?? resp.Content.Headers.ContentLength
                         ?? 0;

            if (assignItemFileName && string.IsNullOrWhiteSpace(_item.FileName))
            {
                var cd = resp.Content.Headers.ContentDisposition;
                _item.FileName = cd?.FileNameStar ?? cd?.FileName ?? GuessFileName(_item.Url);
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
                    var saved = JsonSerializer.Deserialize<List<SegmentInfo>>(File.ReadAllText(stateFile));
                    if (saved != null && saved.Count == threadCount)
                    {
                        foreach (var seg in saved)
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
                    Index        = 0,
                    RangeStart   = 0,
                    RangeEnd     = totalBytes > 0 ? totalBytes - 1 : -1,
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
                        Index        = i,
                        RangeStart   = start,
                        RangeEnd     = end,
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
            var tasks = segments
                .Where(s => !s.IsCompleted)
                .Select(seg => DownloadSegmentWithRetryAsync(seg, supportsRange, url));

            await Task.WhenAll(tasks);
        }

        private const int StallTimeoutSeconds = 20;

        private async Task DownloadSegmentWithRetryAsync(SegmentInfo seg, bool supportsRange, string url)
        {
            int attempt = 0;
            while (true)
            {
                _ct.ThrowIfCancellationRequested();
                try
                {
                    await DownloadSegmentAsync(seg, supportsRange, url);
                    return;
                }
                catch (OperationCanceledException) when (_ct.IsCancellationRequested)
                {
                    throw;
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
                seg.BytesWritten = actualLen;

            using var req = new HttpRequestMessage(HttpMethod.Get, url);

            long resumeFrom = seg.RangeStart + seg.BytesWritten;

            bool shouldSetRange = supportsRange
                                  && seg.RangeEnd >= 0
                                  && (seg.RangeStart > 0 || seg.BytesWritten > 0 || seg.RangeEnd < long.MaxValue - 1);

            if (shouldSetRange)
                req.Headers.Range = new RangeHeaderValue(resumeFrom, seg.RangeEnd);

            using var stallCts = CancellationTokenSource.CreateLinkedTokenSource(_ct);
            stallCts.CancelAfter(TimeSpan.FromSeconds(StallTimeoutSeconds));

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, stallCts.Token);
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

            resp.EnsureSuccessStatusCode();

            var contentType = resp.Content.Headers.ContentType?.MediaType ?? "";
            if (IsHtmlContentType(contentType))
            {
                throw new InvalidOperationException(
                    "Server trả về trang HTML thay vì file. " +
                    "Có thể URL yêu cầu đăng nhập hoặc đã hết hạn.");
            }

            bool serverHonoredRange = resp.StatusCode == System.Net.HttpStatusCode.PartialContent;
            var fileMode = (seg.BytesWritten > 0 && serverHonoredRange)
                ? FileMode.Append
                : FileMode.Create;

            if (fileMode == FileMode.Create)
                seg.BytesWritten = 0;

            await using var fs = new FileStream(seg.TempFilePath, fileMode, FileAccess.Write, FileShare.None);
            await using var stream = await resp.Content.ReadAsStreamAsync(_ct);

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
            if (dir != null) Directory.CreateDirectory(dir);

            string mergingPath = destPath + ".merging";

            await using (var output = new FileStream(mergingPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                foreach (var seg in segments.OrderBy(s => s.Index))
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

            var (totalBytes, supportsRange) = await ProbeAsync(url, assignItemFileName: false);

            bool useMultiSegment = supportsRange && totalBytes >= MinSizeForMultiSegment;
            int threadCount = useMultiSegment ? _item.Threads : 1;

            var segments = BuildOrRestoreSegments(subTempDir, totalBytes, threadCount);

            using var speedTimer = new System.Timers.Timer(1000);
            long lastReported = segments.Sum(s => s.BytesWritten);
            speedTimer.Elapsed += (_, _) =>
            {
                long current = segments.Sum(s => s.BytesWritten);
                double speed = current - lastReported;
                lastReported = current;
                _item.DownloadedBytes = progressBaseOffset + current;
                _item.SpeedBps        = speed;
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
                        Directory.Delete(tempDir, true);
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
                if (File.Exists(mergingPath)) File.Delete(mergingPath);
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

        private static string UniqueFilePath(string folder, string name)
        {
            string path = Path.Combine(folder, name);
            if (!File.Exists(path)) return path;

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
                name = name.Replace(c, '_');

            name = name.Trim('"', '\'', ' ');
            return string.IsNullOrWhiteSpace(name) ? "download" : name;
        }

        private static bool IsHtmlContentType(string mediaType)
        {
            if (string.IsNullOrEmpty(mediaType)) return false;
            var t = mediaType.Trim().ToLowerInvariant();
            return t is "text/html" or "application/xhtml+xml" or "text/xhtml";
        }

        private static bool LooksLikeHtml(byte[] buffer, int length)
        {
            if (length < 5) return false;

            int offset = 0;
            if (length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
                offset = 3;
            else if (length >= 2 && ((buffer[0] == 0xFF && buffer[1] == 0xFE) ||
                                      (buffer[0] == 0xFE && buffer[1] == 0xFF)))
                offset = 2;

            int checkLen = Math.Min(length - offset, 100);
            if (checkLen < 5) return false;

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
                return _defaultHttp;

            var handler = new HttpClientHandler
            {
                AllowAutoRedirect        = true,
                MaxAutomaticRedirections = 10,
                UseCookies               = false
            };
            var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };

            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");

            foreach (var (key, value) in customHeaders)
            {
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                    continue;

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
                AllowAutoRedirect        = true,
                MaxAutomaticRedirections = 10,
                UseCookies               = true
            };
            var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
            return client;
        }
    }

    public record DownloadProgress(long DownloadedBytes, double SpeedBps);
}
