namespace PDownloader.Core.Service
{
    public sealed class YtDlpService
    {
        public static readonly YtDlpService Instance = new();
        private YtDlpService() { }

        private string? _resolvedPath;

        public string? FindYtDlp()
        {
            if (_resolvedPath != null) return _resolvedPath;

            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "yt-dlp.exe"),
                Path.Combine(AppContext.BaseDirectory, "yt-dlp"),

                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PDownloader", "yt-dlp.exe"),
            };

            foreach (var c in candidates)
                if (File.Exists(c)) return _resolvedPath = c;

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

        public async Task<YtAnalyzeResult> AnalyzeAsync(string url, CancellationToken ct = default)
        {
            var bin = FindYtDlp();
            if (bin == null)
                return YtAnalyzeResult.Fail(
                    "yt-dlp không tìm thấy. Đặt yt-dlp.exe cạnh PDownloader.exe " +
                    "hoặc thêm vào PATH rồi khởi động lại.");

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

        public static string BuildDownloadArgs(string url, string formatId, string outputPath, int threads = 8)
        {
            int n = Math.Clamp(threads, 1, 16);

            return $"-f \"{EscapeArg(formatId)}\" " +
                   $"--no-warnings " +
                   $"--concurrent-fragments {n} " +
                   $"--http-chunk-size 10M " +
                   $"-o \"{EscapeArg(outputPath)}\" " +
                   $"-- \"{EscapeArg(url)}\"";
        }

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
                if (hasVideo && hasAudio) note = "";
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
}
