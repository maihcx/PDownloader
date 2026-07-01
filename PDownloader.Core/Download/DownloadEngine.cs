using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;


namespace PDownloader.Core.Download;

/// <summary>
/// IDM-style download engine:
///  • HEAD request to check Content-Length and Accept-Ranges
///  • Split into N segments and download in parallel
///  • Resume each segment from its persisted offset (temp .part files)
///  • Merge all segments into the final file
///  • Auto-retry on transient errors with exponential back-off
/// </summary>
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

            await DownloadAllSegmentsAsync(segments, supportsRange);

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
        var bin = YtDlpService.Instance.FindYtDlp();
        if (bin == null)
        {
            _item.Status       = DownloadStatus.Error;
            _item.ErrorMessage = "yt-dlp không tìm thấy.";
            return;
        }

        string folder = string.IsNullOrWhiteSpace(_item.SavePath)
            ? UserDataStore.GetDefaultDownloadFolder()
            : _item.SavePath;

        Directory.CreateDirectory(folder);

        string outputTemplate = Path.Combine(folder, "%(title)s.%(ext)s");
        string args = YtDlpService.BuildDownloadArgs(
            _item.Url,
            _item.FormatId ?? "bestvideo+bestaudio/best",
            outputTemplate);

        _item.Status    = DownloadStatus.Downloading;
        _item.StartTime = DateTime.Now;

        var psi = new ProcessStartInfo
        {
            FileName               = bin,
            Arguments              = args,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding  = System.Text.Encoding.UTF8,
        };

        using var proc = new Process { StartInfo = psi };

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            if (e.Data.StartsWith("[download]"))
                ParseYtDlpProgress(e.Data);
        };

        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
                System.Diagnostics.Debug.WriteLine("[yt-dlp] " + e.Data);
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try { await proc.WaitForExitAsync(_ct); }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        if (proc.ExitCode == 0)
        {
            _item.Status          = DownloadStatus.Completed;
            _item.EndTime         = DateTime.Now;
            _item.DownloadedBytes = _item.TotalBytes;
        }
        else
        {
            _item.Status       = DownloadStatus.Error;
            _item.ErrorMessage = "yt-dlp thất bại (exit code " + proc.ExitCode + ")";
        }
    }

    private void ParseYtDlpProgress(string line)
    {
        try
        {
            var pctMatch = System.Text.RegularExpressions.Regex.Match(line, @"([\d.]+)%");
            if (!pctMatch.Success) return;
            double pct = double.Parse(pctMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

            var sizeMatch = System.Text.RegularExpressions.Regex.Match(line, @"of\s+([\d.]+)(MiB|GiB|KiB)");
            if (sizeMatch.Success)
            {
                double val = double.Parse(sizeMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                _item.TotalBytes = sizeMatch.Groups[2].Value switch
                {
                    "GiB" => (long)(val * 1024 * 1024 * 1024),
                    "MiB" => (long)(val * 1024 * 1024),
                    "KiB" => (long)(val * 1024),
                    _ => 0
                };
            }

            if (_item.TotalBytes > 0)
                _item.DownloadedBytes = (long)(_item.TotalBytes * pct / 100.0);

            var speedMatch = System.Text.RegularExpressions.Regex.Match(line, @"at\s+([\d.]+)(MiB|KiB|GiB)/s");
            if (speedMatch.Success)
            {
                double val = double.Parse(speedMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                _item.SpeedBps = speedMatch.Groups[2].Value switch
                {
                    "GiB" => val * 1024 * 1024 * 1024,
                    "MiB" => val * 1024 * 1024,
                    "KiB" => val * 1024,
                    _ => 0
                };
            }
        }
        catch { }
    }

    private async Task<(long totalBytes, bool supportsRange)> ProbeAsync(string url)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            using var resp = await _http.SendAsync(req, _ct);
            resp.EnsureSuccessStatusCode();

            long total = resp.Content.Headers.ContentLength ?? 0;
            bool ranges = resp.Headers.AcceptRanges.Contains("bytes");

            if (string.IsNullOrWhiteSpace(_item.FileName))
            {
                var cd = resp.Content.Headers.ContentDisposition;
                _item.FileName = cd?.FileNameStar ?? cd?.FileName ?? GuessFileName(_item.Url);
                _item.FileName = SanitizeFileName(_item.FileName);
            }

            return (total, ranges);
        }
        catch
        {
            if (string.IsNullOrWhiteSpace(_item.FileName))
                _item.FileName = SanitizeFileName(GuessFileName(_item.Url));
            return (0, false);
        }
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
                RangeEnd     = totalBytes > 0 ? totalBytes - 1 : -1, // -1 = không biết kích thước
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

    private async Task DownloadAllSegmentsAsync(List<SegmentInfo> segments, bool supportsRange)
    {
        var tasks = segments
            .Where(s => !s.IsCompleted)
            .Select(seg => DownloadSegmentWithRetryAsync(seg, supportsRange));

        await Task.WhenAll(tasks);
    }

    private async Task DownloadSegmentWithRetryAsync(SegmentInfo seg, bool supportsRange)
    {
        int attempt = 0;
        while (true)
        {
            _ct.ThrowIfCancellationRequested();
            try
            {
                await DownloadSegmentAsync(seg, supportsRange);
                return;
            }
            catch (OperationCanceledException) { throw; }
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

    private async Task DownloadSegmentAsync(SegmentInfo seg, bool supportsRange)
    {
        long actualLen = File.Exists(seg.TempFilePath)
            ? new FileInfo(seg.TempFilePath).Length
            : 0;
        if (actualLen != seg.BytesWritten)
            seg.BytesWritten = actualLen;

        using var req = new HttpRequestMessage(HttpMethod.Get, _item.Url);

        long resumeFrom = seg.RangeStart + seg.BytesWritten;

        bool shouldSetRange = supportsRange
                              && seg.RangeEnd >= 0
                              && (seg.RangeStart > 0 || seg.BytesWritten > 0 || seg.RangeEnd < long.MaxValue - 1);

        if (shouldSetRange)
            req.Headers.Range = new RangeHeaderValue(resumeFrom, seg.RangeEnd);

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, _ct);

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
        int firstRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), _ct);
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
        while ((read = await stream.ReadAsync(buffer, _ct)) > 0)
        {
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
        string? dir = Path.GetDirectoryName(finalPath);
        if (dir != null) Directory.CreateDirectory(dir);

        string mergingPath = finalPath + ".merging";

        await using (var output = new FileStream(mergingPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            foreach (var seg in segments.OrderBy(s => s.Index))
            {
                await using var input = new FileStream(seg.TempFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                await input.CopyToAsync(output, _ct);
            }
        }

        File.Move(mergingPath, finalPath, overwrite: true);

        foreach (var seg in segments)
        {
            try { File.Delete(seg.TempFilePath); } catch { }
        }

        _item.SavePath = finalPath;
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
                ? UserDataStore.GetDefaultDownloadFolder()
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
            ? UserDataStore.GetDefaultDownloadFolder()
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
