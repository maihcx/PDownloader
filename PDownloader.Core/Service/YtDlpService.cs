using System.Text.Json.Serialization;

namespace PDownloader.Core.Service;

/// <summary>
/// Wraps yt-dlp as a subprocess.
/// Resolves the binary from (in order):
///   1. Folder containing the current exe  (bundled)
///   2. PATH
///   3. %LOCALAPPDATA%\PDownloader\yt-dlp.exe  (user-placed)
/// </summary>
public sealed class YtDlpService
{
    // ── Singleton ────────────────────────────────────────────────────────────
    public static readonly YtDlpService Instance = new();
    private YtDlpService() { }

    // ── Binary resolution ─────────────────────────────────────────────────────
    private string? _resolvedPath;

    /// <summary>Returns the full path to yt-dlp.exe, or null if not found.</summary>
    public string? FindYtDlp()
    {
        if (_resolvedPath != null) return _resolvedPath;

        var candidates = new[]
        {
            // 1. Next to our own exe
            Path.Combine(AppContext.BaseDirectory, "yt-dlp.exe"),
            Path.Combine(AppContext.BaseDirectory, "yt-dlp"),

            // 2. User data folder
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PDownloader", "yt-dlp.exe"),

            // 3. PATH — locate via where.exe / which
        };

        foreach (var c in candidates)
            if (File.Exists(c)) return _resolvedPath = c;

        // Try PATH
        var fromPath = LocateOnPath("yt-dlp.exe") ?? LocateOnPath("yt-dlp");
        if (fromPath != null) return _resolvedPath = fromPath;

        return null;
    }

    private static string? LocateOnPath(string name)
    {
        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator)
                    ?? Array.Empty<string>();
        foreach (var dir in paths)
        {
            var full = Path.Combine(dir, name);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    // ── Analyze ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs: yt-dlp -J --no-warnings --no-playlist {url}
    /// Returns a structured result the HTTP bridge can serialise directly.
    /// </summary>
    public async Task<YtAnalyzeResult> AnalyzeAsync(string url, CancellationToken ct = default)
    {
        var bin = FindYtDlp();
        if (bin == null)
            return YtAnalyzeResult.Fail(
                "yt-dlp không tìm thấy. Đặt yt-dlp.exe cạnh PDownloader.exe " +
                "hoặc thêm vào PATH rồi khởi động lại.");

        // --dump-single-json = -J ; abort playlist so we only get one video
        var (stdout, stderr, exitCode) = await RunAsync(
            bin,
            $"-J --no-warnings --no-playlist -- \"{EscapeArg(url)}\"",
            ct);

        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            var err = ParseYtDlpError(stderr);
            return YtAnalyzeResult.Fail(err);
        }

        try
        {
            using var doc = JsonDocument.Parse(stdout);
            return ParseFormats(doc.RootElement);
        }
        catch (Exception ex)
        {
            return YtAnalyzeResult.Fail($"Lỗi parse JSON từ yt-dlp: {ex.Message}");
        }
    }

    // ── Download ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a yt-dlp command and returns the argument string.
    /// The actual process is started by the caller (e.g. CoreDownloadService)
    /// so it can be tracked, cancelled, and reported in the downloads list.
    /// </summary>
    public static string BuildDownloadArgs(string url, string formatId, string outputPath)
    {
        // outputPath must include filename template, e.g. C:\Videos\%(title)s.%(ext)s
        return $"-f \"{EscapeArg(formatId)}\" " +
               $"--merge-output-format mp4 " +
               $"--no-warnings " +
               $"-o \"{EscapeArg(outputPath)}\" " +
               $"-- \"{EscapeArg(url)}\"";
    }

    // ── Format parsing ────────────────────────────────────────────────────────

    private static YtAnalyzeResult ParseFormats(JsonElement root)
    {
        var title = root.GetStringOrDefault("title") ?? "video";

        if (!root.TryGetProperty("formats", out var fmtArray) ||
            fmtArray.ValueKind != JsonValueKind.Array)
            return YtAnalyzeResult.Fail("yt-dlp không trả về danh sách formats.");

        var formats = new List<YtFormat>();

        foreach (var f in fmtArray.EnumerateArray())
        {
            var id = f.GetStringOrDefault("format_id") ?? "";
            var ext = f.GetStringOrDefault("ext") ?? "mp4";

            // Skip storyboard / mhtml thumbnails
            if (ext is "mhtml" or "none") continue;

            var vcodec = f.GetStringOrDefault("vcodec") ?? "none";
            var acodec = f.GetStringOrDefault("acodec") ?? "none";

            bool hasVideo = vcodec != "none";
            bool hasAudio = acodec != "none";

            int? height = f.TryGetProperty("height", out var hProp) && hProp.ValueKind == JsonValueKind.Number
                ? hProp.GetInt32() : null;

            long filesize = 0;
            if (f.TryGetProperty("filesize", out var fsProp) && fsProp.ValueKind == JsonValueKind.Number)
                filesize = fsProp.GetInt64();
            else if (f.TryGetProperty("filesize_approx", out var fsaProp) && fsaProp.ValueKind == JsonValueKind.Number)
                filesize = fsaProp.GetInt64();

            string note;
            if (hasVideo && hasAudio) note = "";            // muxed
            else if (hasVideo) note = "Video Only";
            else note = "Audio Only";

            formats.Add(new YtFormat
            {
                Id       = id,
                Ext      = ext,
                Height   = height,
                Note     = note,
                Filesize = filesize,
                Size     = FormatSize(filesize),
            });
        }

        // Sort: muxed (best quality) first, then video-only desc, then audio-only
        formats = formats
            .OrderBy(f => f.Note == "" ? 0 :
                          f.Note == "Video Only" ? 1 : 2)
            .ThenByDescending(f => f.Height ?? 0)
            .ThenByDescending(f => f.Filesize)
            .ToList();

        return new YtAnalyzeResult { Success = true, Title = title, Formats = formats };
    }

    // ── Subprocess helper ─────────────────────────────────────────────────────

    private static async Task<(string stdout, string stderr, int exitCode)>
        RunAsync(string bin, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = bin,
            Arguments              = args,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8,
        };

        using var proc = new Process { StartInfo = psi };

        var stdoutSb = new StringBuilder();
        var stderrSb = new StringBuilder();

        proc.OutputDataReceived += (_, e) => { if (e.Data != null) stdoutSb.AppendLine(e.Data); };
        proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) stderrSb.AppendLine(e.Data); };

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

        return (stdoutSb.ToString(), stderrSb.ToString(), proc.ExitCode);
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    private static string ParseYtDlpError(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
            return "yt-dlp thất bại (không có thông tin lỗi).";

        // Extract the last ERROR: line for a concise message
        var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var errorLine = lines.LastOrDefault(l =>
            l.TrimStart().StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase));

        return errorLine?.TrimStart() ?? stderr.Trim()[..Math.Min(200, stderr.Trim().Length)];
    }

    private static string EscapeArg(string s) => s.Replace("\"", "\\\"");

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

public sealed class YtAnalyzeResult
{
    [JsonPropertyName("success")] public bool Success { get; init; }
    [JsonPropertyName("title")] public string? Title { get; init; }
    [JsonPropertyName("formats")] public List<YtFormat>? Formats { get; init; }
    [JsonPropertyName("error")] public string? Error { get; init; }

    public static YtAnalyzeResult Fail(string error) =>
        new() { Success = false, Error = error };
}

public sealed class YtFormat
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("ext")] public string Ext { get; init; } = "";
    [JsonPropertyName("height")] public int? Height { get; init; }
    [JsonPropertyName("note")] public string Note { get; init; } = "";
    [JsonPropertyName("size")] public string Size { get; init; } = "";
    [JsonPropertyName("filesize")] public long Filesize { get; init; }
}

// ── JsonElement extension ─────────────────────────────────────────────────────
internal static class JsonElementExt
{
    public static string? GetStringOrDefault(this JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() : null;
}