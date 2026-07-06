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

namespace PDownloader.Core.Service;

public sealed class YtDlpService
{
    public static readonly YtDlpService Instance = new();
    private YtDlpService() { }

    private string? _resolvedPath;

    private string? _resolvedPathQjs;

    public string? FindYtDlp()
    {
        if (_resolvedPath != null)
        {
            return _resolvedPath;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "yt-dlp.exe"),
            Path.Combine(AppContext.BaseDirectory, "yt-dlp"),

            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PDownloader", "yt-dlp.exe"),
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c))
            {
                return _resolvedPath = c;
            }
        }

        var fromPath = LocateOnPath("yt-dlp.exe") ?? LocateOnPath("yt-dlp");
        if (fromPath != null)
        {
            return _resolvedPath = fromPath;
        }

        return null;
    }

    public string? FindQJS()
    {
        if (_resolvedPathQjs != null)
        {
            return _resolvedPathQjs;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "qjs.exe"),
            Path.Combine(AppContext.BaseDirectory, "qjs"),

            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PDownloader", "qjs.exe"),
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c))
            {
                return _resolvedPathQjs = c;
            }
        }

        var fromPath = LocateOnPath("qjs.exe") ?? LocateOnPath("qjs");
        if (fromPath != null)
        {
            return _resolvedPathQjs = fromPath;
        }

        return null;
    }

    private static string? LocateOnPath(string name)
    {
        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator)
                    ?? Array.Empty<string>();
        foreach (var dir in paths)
        {
            var full = Path.Combine(dir, name);
            if (File.Exists(full))
            {
                return full;
            }
        }

        return null;
    }

    private string? _resolvedFfmpegPath;

    public string? FindFfmpeg()
    {
        if (_resolvedFfmpegPath != null)
        {
            return _resolvedFfmpegPath;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"),
            Path.Combine(AppContext.BaseDirectory, "ffmpeg"),

            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PDownloader", "ffmpeg.exe"),
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c))
            {
                return _resolvedFfmpegPath = c;
            }
        }

        var fromPath = LocateOnPath("ffmpeg.exe") ?? LocateOnPath("ffmpeg");
        if (fromPath != null)
        {
            return _resolvedFfmpegPath = fromPath;
        }

        return null;
    }

    public sealed class ResolvedStream
    {
        public string Url { get; set; } = string.Empty;
        public string Ext { get; set; } = "mp4";
        public bool HasVideo { get; set; }
        public bool HasAudio { get; set; }
        public long FilesizeApprox { get; set; }
    }

    private static string? WriteNetscapeCookieFile(string? cookieHeader)
    {
        if (string.IsNullOrWhiteSpace(cookieHeader))
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.AppendLine("# Netscape HTTP Cookie File");

        const string expiry = "2147483647";

        foreach (string pair in cookieHeader.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            string name = pair[..eq].Trim();
            string value = pair[(eq + 1)..].Trim();

            if (name.Length == 0)
            {
                continue;
            }

            sb.Append(".youtube.com").Append('\t')
              .Append("TRUE").Append('\t')
              .Append('/').Append('\t')
              .Append("TRUE").Append('\t')
              .Append(expiry).Append('\t')
              .Append(name).Append('\t')
              .Append(value).Append('\n');
        }

        string path = Path.Combine(Path.GetTempPath(), $"pdownloader_cookies_{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static void DeleteCookieFileSafe(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try { File.Delete(path); } catch { /* best effort */ }
    }

    public async Task<List<ResolvedStream>> ResolveDirectUrlsAsync(
        string pageUrl, string formatId, string? referer, string? cookieHeader = null, CancellationToken ct = default)
    {
        var bin = FindYtDlp()
            ?? throw new InvalidOperationException("yt-dlp không tìm thấy.");

        string refererArg = string.IsNullOrWhiteSpace(referer)
            ? ""
            : $"--referer \"{EscapeArg(referer)}\" ";

        string qjsBin = FindQJS() 
            ?? throw new InvalidOperationException("qjs không tìm thấy.");

        string qjsArg = string.IsNullOrWhiteSpace(qjsBin)
            ? ""
            : $"--js-runtimes quickjs:\"{qjsBin}\" ";

        string? cookieFile = WriteNetscapeCookieFile(cookieHeader);
        string cookieArg = cookieFile == null
            ? ""
            : $"--cookies \"{EscapeArg(cookieFile)}\" ";

        try
        {
            string args = $"-f \"{EscapeArg(formatId)}\" -j --no-warnings --no-playlist " +
                          refererArg +
                          qjsArg +
                          cookieArg +
                          $"-- \"{EscapeArg(pageUrl)}\"";

            (string? stdout, string? stderr, int exitCode) = await RunAsync(bin, args, ct);

            if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            {
                throw new InvalidOperationException(ParseYtDlpError(stderr));
            }

            string? jsonLine = stdout
                .Split('\n')
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.StartsWith("{"));

            if (jsonLine == null)
            {
                throw new InvalidOperationException("yt-dlp không trả về JSON hợp lệ.");
            }

            using var doc = JsonDocument.Parse(jsonLine);
            JsonElement root = doc.RootElement;

            var results = new List<ResolvedStream>();

            if (root.TryGetProperty("requested_formats", out JsonElement rf) &&
                rf.ValueKind == JsonValueKind.Array && rf.GetArrayLength() > 0)
            {
                foreach (JsonElement el in rf.EnumerateArray())
                {
                    results.Add(ParseResolvedStream(el));
                }
            }
            else if (root.TryGetProperty("requested_downloads", out JsonElement rd) &&
                rd.ValueKind == JsonValueKind.Array && rd.GetArrayLength() > 0)
            {
                foreach (JsonElement el in rd.EnumerateArray())
                {
                    results.Add(ParseResolvedStream(el));
                }
            }
            else
            {
                results.Add(ParseResolvedStream(root));
            }

            results.RemoveAll(r => string.IsNullOrWhiteSpace(r.Url));

            if (results.Count == 0)
            {
                string preview = jsonLine.Length > 300 ? jsonLine[..300] + "..." : jsonLine;
                throw new InvalidOperationException(
                    "Không tìm thấy URL trực tiếp trong JSON yt-dlp trả về. JSON (rút gọn): " + preview);
            }

            return results;
        }
        finally
        {
            DeleteCookieFileSafe(cookieFile);
        }
    }

    private static ResolvedStream ParseResolvedStream(JsonElement el)
    {
        string url = el.GetStringOrDefault("url") ?? "";
        string ext = el.GetStringOrDefault("ext") ?? "mp4";
        string vcodec = el.GetStringOrDefault("vcodec") ?? "none";
        string acodec = el.GetStringOrDefault("acodec") ?? "none";

        long filesize = 0;
        if (el.TryGetProperty("filesize", out JsonElement fs) && fs.ValueKind == JsonValueKind.Number)
        {
            filesize = fs.GetInt64();
        }
        else if (el.TryGetProperty("filesize_approx", out JsonElement fsa) && fsa.ValueKind == JsonValueKind.Number)
        {
            filesize = fsa.GetInt64();
        }

        return new ResolvedStream
        {
            Url = url,
            Ext = ext,
            HasVideo = !string.Equals(vcodec, "none", StringComparison.OrdinalIgnoreCase),
            HasAudio = !string.Equals(acodec, "none", StringComparison.OrdinalIgnoreCase),
            FilesizeApprox = filesize,
        };
    }

    public async Task<YtAnalyzeResult> AnalyzeAsync(string url, string? cookieHeader = null, CancellationToken ct = default)
    {
        var bin = FindYtDlp();
        if (bin == null)
        {
            return YtAnalyzeResult.Fail(
                "yt-dlp không tìm thấy. Đặt yt-dlp.exe cạnh PDownloader.exe " +
                "hoặc thêm vào PATH rồi khởi động lại.");
        }

        string qjsBin = FindQJS()
            ?? throw new InvalidOperationException("qjs không tìm thấy.");

        string qjsArg = string.IsNullOrWhiteSpace(qjsBin)
            ? ""
            : $"--js-runtimes quickjs:\"{qjsBin}\" ";

        string? cookieFile = WriteNetscapeCookieFile(cookieHeader);
        string cookieArg = cookieFile == null
            ? ""
            : $"--cookies \"{EscapeArg(cookieFile)}\" ";

        try
        {
            (string? stdout, string? stderr, int exitCode) = await RunAsync(
                bin,
                $"-J --no-warnings --no-playlist " + qjsArg + cookieArg + $"-- \"{EscapeArg(url)}\"",
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
        finally
        {
            DeleteCookieFileSafe(cookieFile);
        }
    }

    private static YtAnalyzeResult ParseFormats(JsonElement root)
    {
        var title = root.GetStringOrDefault("title") ?? "video";

        if (!root.TryGetProperty("formats", out JsonElement fmtArray) ||
            fmtArray.ValueKind != JsonValueKind.Array)
        {
            return YtAnalyzeResult.Fail("yt-dlp không trả về danh sách formats.");
        }

        var formats = new List<YtFormat>();

        foreach (JsonElement f in fmtArray.EnumerateArray())
        {
            var id = f.GetStringOrDefault("format_id") ?? "";
            var ext = f.GetStringOrDefault("ext") ?? "mp4";

            // Skip storyboard / mhtml thumbnails
            if (ext is "mhtml" or "none")
            {
                continue;
            }

            var vcodec = f.GetStringOrDefault("vcodec") ?? "none";
            var acodec = f.GetStringOrDefault("acodec") ?? "none";

            bool hasVideo = vcodec != "none";
            bool hasAudio = acodec != "none";

            int? height = f.TryGetProperty("height", out JsonElement hProp) && hProp.ValueKind == JsonValueKind.Number
                ? hProp.GetInt32() : null;

            long filesize = 0;
            if (f.TryGetProperty("filesize", out JsonElement fsProp) && fsProp.ValueKind == JsonValueKind.Number)
            {
                filesize = fsProp.GetInt64();
            }
            else if (f.TryGetProperty("filesize_approx", out JsonElement fsaProp) && fsaProp.ValueKind == JsonValueKind.Number)
            {
                filesize = fsaProp.GetInt64();
            }

            string note;
            if (hasVideo && hasAudio)
            {
                note = "";
            }
            else if (hasVideo)
            {
                note = "Video Only";
            }
            else
            {
                note = "Audio Only";
            }

            formats.Add(new YtFormat
            {
                Id = id,
                Ext = ext,
                Height = height,
                Note = note,
                Filesize = filesize,
                Size = FormatSize(filesize),
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

        var stdoutSb = new StringBuilder();
        var stderrSb = new StringBuilder();

        proc.OutputDataReceived += (_, e) => { if (e.Data != null) { stdoutSb.AppendLine(e.Data); } };
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

        return (stdoutSb.ToString(), stderrSb.ToString(), proc.ExitCode);
    }

    private static string ParseYtDlpError(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return "yt-dlp thất bại (không có thông tin lỗi).";
        }

        // Extract the last ERROR: line for a concise message
        var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var errorLine = lines.LastOrDefault(l =>
            l.TrimStart().StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase));

        return errorLine?.TrimStart() ?? stderr.Trim()[..Math.Min(200, stderr.Trim().Length)];
    }

    private static string EscapeArg(string s) => s.Replace("\"", "\\\"");

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:F1} KB";
        }

        if (bytes < 1024 * 1024 * 1024)
        {
            return $"{bytes / (1024.0 * 1024):F1} MB";
        }

        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}