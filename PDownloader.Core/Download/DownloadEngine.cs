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
    private const int    MaxRetries    = 5;
    private const int    BufferSize    = 81920;      // 80 KB read buffer
    private const string StateExt      = ".pdstate"; // persisted segment state

    private readonly DownloadItem          _item;
    private readonly IProgress<DownloadProgress> _progress;
    private readonly CancellationToken     _ct;
    private static readonly HttpClient     _http = CreateHttpClient();

    public DownloadEngine(DownloadItem item, IProgress<DownloadProgress> progress, CancellationToken ct)
    {
        _item     = item;
        _progress = progress;
        _ct       = ct;
    }

    // ── Public entry point ────────────────────────────────────────────────────
    public async Task RunAsync()
    {
        if (_item.IsYoutube)
        {
            await RunYtDlpAsync();
            return;
        }

        string tempDir = GetTempDir();
        Directory.CreateDirectory(tempDir);

        // 1. HEAD — discover total size and range support
        var (totalBytes, supportsRange) = await ProbeAsync(_item.Url);
        _item.TotalBytes = totalBytes;

        int threadCount = (supportsRange && totalBytes > 0) ? _item.Threads : 1;

        // 2. Build or restore segment list
        var segments = BuildOrRestoreSegments(tempDir, totalBytes, threadCount);

        // 3. Download segments in parallel
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

        await DownloadAllSegmentsAsync(segments, supportsRange);

        speedTimer.Stop();

        _ct.ThrowIfCancellationRequested();

        // 4. Merge
        _item.Status = DownloadStatus.Merging;
        await MergeSegmentsAsync(segments);

        // 5. Cleanup
        CleanupTemp(tempDir);

        _item.DownloadedBytes = _item.TotalBytes;
        _item.SpeedBps = 0;
        _item.Status   = DownloadStatus.Completed;
        _item.EndTime  = DateTime.Now;
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

        // Dùng output template để yt-dlp tự đặt tên file
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

        // Parse tiến độ từ stdout yt-dlp: "[download]  23.4% of  142.31MiB at  3.20MiB/s"
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
            _item.Status  = DownloadStatus.Completed;
            _item.EndTime = DateTime.Now;
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
            // % 
            var pctMatch = System.Text.RegularExpressions.Regex.Match(line, @"([\d.]+)%");
            if (!pctMatch.Success) return;
            double pct = double.Parse(pctMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

            // total size
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

            // speed
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

    // ── HEAD probe ────────────────────────────────────────────────────────────
    private async Task<(long totalBytes, bool supportsRange)> ProbeAsync(string url)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            using var resp = await _http.SendAsync(req, _ct);
            resp.EnsureSuccessStatusCode();

            long total = resp.Content.Headers.ContentLength ?? 0;
            bool ranges = resp.Headers.AcceptRanges.Contains("bytes")
                       || (resp.Content.Headers.ContentLength.HasValue && resp.Content.Headers.ContentLength > 0);

            // Infer file name from Content-Disposition if not set
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
            // Fall back to single-stream with unknown size
            if (string.IsNullOrWhiteSpace(_item.FileName))
                _item.FileName = SanitizeFileName(GuessFileName(_item.Url));
            return (0, false);
        }
    }

    // ── Segment building / restore ────────────────────────────────────────────
    private List<SegmentInfo> BuildOrRestoreSegments(string tempDir, long totalBytes, int threadCount)
    {
        string stateFile = Path.Combine(tempDir, "segments" + StateExt);

        if (File.Exists(stateFile))
        {
            try
            {
                var saved = JsonSerializer.Deserialize<List<SegmentInfo>>(File.ReadAllText(stateFile));
                if (saved != null && saved.Count == threadCount) return saved;
            }
            catch { }
        }

        var segments = new List<SegmentInfo>();

        if (totalBytes <= 0 || threadCount == 1)
        {
            segments.Add(new SegmentInfo
            {
                Index        = 0,
                RangeStart   = 0,
                RangeEnd     = totalBytes > 0 ? totalBytes - 1 : long.MaxValue,
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
                long end   = (i == threadCount - 1) ? totalBytes - 1 : start + chunkSize - 1;
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

    // ── Parallel segment download ─────────────────────────────────────────────
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
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < MaxRetries)
            {
                attempt++;
                int delay = (int)Math.Pow(2, attempt) * 500; // 1s, 2s, 4s, 8s, 16s
                System.Diagnostics.Debug.WriteLine($"[Runner] Segment {seg.Index} attempt {attempt} failed: {ex.Message}. Retry in {delay}ms");
                await Task.Delay(delay, _ct);
            }
        }
    }

    private async Task DownloadSegmentAsync(SegmentInfo seg, bool supportsRange)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, _item.Url);

        long resumeFrom = seg.RangeStart + seg.BytesWritten;

        if (supportsRange && seg.RangeEnd != long.MaxValue)
        {
            req.Headers.Range = new RangeHeaderValue(resumeFrom, seg.RangeEnd);
        }

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, _ct);
        resp.EnsureSuccessStatusCode();

        // Open temp file in append mode if resuming
        var fileMode = seg.BytesWritten > 0 ? FileMode.Append : FileMode.Create;
        await using var fs = new FileStream(seg.TempFilePath, fileMode, FileAccess.Write, FileShare.None);
        await using var stream = await resp.Content.ReadAsStreamAsync(_ct);

        byte[] buffer = new byte[BufferSize];
        int read;
        while ((read = await stream.ReadAsync(buffer, _ct)) > 0)
        {
            _ct.ThrowIfCancellationRequested();
            await fs.WriteAsync(buffer.AsMemory(0, read), _ct);
            seg.BytesWritten += read;
        }
    }

    // ── Merge ─────────────────────────────────────────────────────────────────
    private async Task MergeSegmentsAsync(List<SegmentInfo> segments)
    {
        string finalPath = GetFinalPath();
        string? dir = Path.GetDirectoryName(finalPath);
        if (dir != null) Directory.CreateDirectory(dir);

        await using var output = new FileStream(finalPath, FileMode.Create, FileAccess.Write, FileShare.None);

        foreach (var seg in segments.OrderBy(s => s.Index))
        {
            if (!File.Exists(seg.TempFilePath)) continue;

            await using (var input = new FileStream(seg.TempFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await input.CopyToAsync(output, _ct);
            }

            try { File.Delete(seg.TempFilePath); } catch { }
        }

        _item.SavePath = finalPath;
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────
    private void CleanupTemp(string tempDir)
    {
        try { Directory.Delete(tempDir, true); }
        catch { /* best-effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private string GetTempDir()
    {
        string baseTmp = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SM SOFT", "PDownloader", "Temp", _item.Id);
        return baseTmp;
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
        string ext   = Path.GetExtension(name);
        int counter  = 1;
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
            var uri  = new Uri(url);
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
        // Remove surrounding quotes from Content-Disposition
        name = name.Trim('"', '\'', ' ');
        return string.IsNullOrWhiteSpace(name) ? "download" : name;
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect    = true,
            MaxAutomaticRedirections = 10,
            UseCookies           = true
        };
        var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
        return client;
    }
}

public record DownloadProgress(long DownloadedBytes, double SpeedBps);
