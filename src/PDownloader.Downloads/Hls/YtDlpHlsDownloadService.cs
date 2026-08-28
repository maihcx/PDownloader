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

namespace PDownloader.Downloads.Hls;

internal sealed class YtDlpHlsDownloadService
{
    private const string ProgressPrefix = "__PD_PROGRESS__|";
    private const string DestinationPrefix = "__PD_DESTINATION__|";

    private const string ProgressTemplate =
        "download:" + ProgressPrefix +
        "%(progress.downloaded_bytes)s|" +
        "%(progress.total_bytes)s|" +
        "%(progress.total_bytes_estimate)s|" +
        "%(progress.speed)s";

    private readonly YtDlpService _ytDlpService;

    public YtDlpHlsDownloadService(YtDlpService ytDlpService)
    {
        _ytDlpService = ytDlpService ?? throw new ArgumentNullException(nameof(ytDlpService));
    }

    public async Task<string> DownloadAsync(
        string url,
        string? formatId,
        string tempDirectory,
        string outputPathWithoutExtension,
        string? referer,
        string? cookieHeader,
        string? cookieJarJson,
        string? userAgent,
        IReadOnlyDictionary<string, string>? extraHeaders,
        int preferredFragmentCount,
        Action<long, long, double>? reportProgress,
        CancellationToken cancellationToken)
    {
        string ytDlpPath = _ytDlpService.FindYtDlp()
            ?? throw new InvalidOperationException("yt-dlp không tìm thấy.");

        string fileStem = Path.GetFileName(outputPathWithoutExtension);
        string temporaryOutputWithoutExtension = Path.Combine(tempDirectory, fileStem);
        string? cookieFile = _ytDlpService.CreateCookieFile(
            cookieHeader,
            url,
            cookieJarJson);

        try
        {
            ProcessStartInfo startInfo = BuildStartInfo(
                ytDlpPath,
                url,
                formatId,
                temporaryOutputWithoutExtension,
                referer,
                userAgent,
                extraHeaders,
                cookieFile,
                preferredFragmentCount);

            YtDlpProcessResult processResult = await RunProcessAsync(
                startInfo,
                reportProgress,
                cancellationToken);

            if (processResult.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    YtDlpErrorParser.Parse(processResult.StandardError));
            }

            string temporaryResultPath = ResolveTemporaryResultPath(
                processResult.DestinationPath,
                tempDirectory,
                fileStem);

            string extension = Path.GetExtension(temporaryResultPath);
            string destinationPath = outputPathWithoutExtension + extension;
            if (File.Exists(destinationPath))
            {
                destinationPath = DownloadPathService.UniqueFilePath(
                    Path.GetDirectoryName(destinationPath) ?? string.Empty,
                    Path.GetFileName(destinationPath));
            }
            else
            {
                string? destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (destinationDirectory != null)
                {
                    Directory.CreateDirectory(destinationDirectory);
                }
            }

            File.Move(temporaryResultPath, destinationPath, overwrite: true);
            return destinationPath;
        }
        finally
        {
            _ytDlpService.DeleteCookieFile(cookieFile);
        }
    }

    private static ProcessStartInfo BuildStartInfo(
        string ytDlpPath,
        string url,
        string? formatId,
        string outputPathWithoutExtension,
        string? referer,
        string? userAgent,
        IReadOnlyDictionary<string, string>? extraHeaders,
        string? cookieFile,
        int preferredFragmentCount)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ytDlpPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        startInfo.ArgumentList.Add("--newline");
        startInfo.ArgumentList.Add("--no-warnings");
        startInfo.ArgumentList.Add("--no-playlist");

        if (!string.IsNullOrWhiteSpace(formatId))
        {
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add(formatId);
        }

        startInfo.ArgumentList.Add("--progress");
        startInfo.ArgumentList.Add("--progress-delta");
        startInfo.ArgumentList.Add("0.25");
        startInfo.ArgumentList.Add("--progress-template");
        startInfo.ArgumentList.Add(ProgressTemplate);
        startInfo.ArgumentList.Add("--print");
        startInfo.ArgumentList.Add(
            $"after_move:{DestinationPrefix}%(filepath)s");
        startInfo.ArgumentList.Add("--no-simulate");

        startInfo.ArgumentList.Add("--merge-output-format");
        startInfo.ArgumentList.Add("mp4");
        startInfo.ArgumentList.Add("--hls-prefer-native");
        startInfo.ArgumentList.Add("--concurrent-fragments");
        startInfo.ArgumentList.Add(
            Math.Clamp(preferredFragmentCount, 1, 16)
                .ToString(CultureInfo.InvariantCulture));

        if (!string.IsNullOrWhiteSpace(referer))
        {
            startInfo.ArgumentList.Add("--add-header");
            startInfo.ArgumentList.Add($"Referer:{referer}");
        }

        if (!string.IsNullOrWhiteSpace(userAgent))
        {
            startInfo.ArgumentList.Add("--user-agent");
            startInfo.ArgumentList.Add(userAgent);
        }

        AddForwardedHeaders(startInfo, extraHeaders);

        if (!string.IsNullOrWhiteSpace(cookieFile))
        {
            startInfo.ArgumentList.Add("--cookies");
            startInfo.ArgumentList.Add(cookieFile);
        }

        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(outputPathWithoutExtension + ".%(ext)s");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(url);

        return startInfo;
    }

    private static void AddForwardedHeaders(
        ProcessStartInfo startInfo,
        IReadOnlyDictionary<string, string>? extraHeaders)
    {
        if (extraHeaders == null || extraHeaders.Count == 0)
        {
            return;
        }

        string[] allowedNames =
        {
            "Accept",
            "Accept-Language",
            "Authorization",
            "Origin",
        };

        foreach (string name in allowedNames)
        {
            string? value = GetHeader(extraHeaders, name);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            startInfo.ArgumentList.Add("--add-header");
            startInfo.ArgumentList.Add($"{name}:{value}");
        }
    }

    private static string? GetHeader(
        IReadOnlyDictionary<string, string> headers,
        string name)
    {
        foreach ((string key, string value) in headers)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    private static async Task<YtDlpProcessResult> RunProcessAsync(
        ProcessStartInfo startInfo,
        Action<long, long, double>? reportProgress,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        var standardError = new StringBuilder();
        var totalSizeTracker = new StableTotalSizeTracker();
        string? destinationPath = null;

        process.OutputDataReceived += (_, args) =>
        {
            string? line = args.Data;
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            if (TryParseProgress(line, out YtDlpProgressSnapshot progress))
            {
                long stableTotalBytes = totalSizeTracker.GetStableTotalBytes(progress);

                reportProgress?.Invoke(
                    progress.DownloadedBytes,
                    stableTotalBytes,
                    progress.SpeedBps);
                return;
            }

            if (line.StartsWith(DestinationPrefix, StringComparison.Ordinal))
            {
                string path = line[DestinationPrefix.Length..].Trim();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    destinationPath = path;
                }
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data != null)
            {
                standardError.AppendLine(args.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }

            throw;
        }

        return new YtDlpProcessResult(
            destinationPath,
            standardError.ToString(),
            process.ExitCode);
    }

    private static bool TryParseProgress(
        string line,
        out YtDlpProgressSnapshot progress)
    {
        progress = default;

        if (!line.StartsWith(ProgressPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        string[] values = line[ProgressPrefix.Length..].Split('|');
        if (values.Length < 4
            || !TryParseNonNegativeInt64(values[0], out long downloadedBytes))
        {
            return false;
        }

        _ = TryParseNonNegativeInt64(values[1], out long exactTotalBytes);
        _ = TryParseNonNegativeInt64(values[2], out long estimatedTotalBytes);
        _ = TryParseNonNegativeDouble(values[3], out double speedBps);

        progress = new YtDlpProgressSnapshot(
            downloadedBytes,
            exactTotalBytes,
            estimatedTotalBytes,
            speedBps);
        return true;
    }

    private static bool TryParseNonNegativeInt64(string value, out long result)
    {
        value = value.Trim();

        if (long.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long integerValue)
            && integerValue >= 0)
        {
            result = integerValue;
            return true;
        }

        if (double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double floatingValue)
            && double.IsFinite(floatingValue)
            && floatingValue >= 0)
        {
            result = floatingValue >= long.MaxValue
                ? long.MaxValue
                : (long)floatingValue;
            return true;
        }

        result = 0;
        return false;
    }

    private static bool TryParseNonNegativeDouble(string value, out double result)
    {
        if (double.TryParse(
                value.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double parsed)
            && double.IsFinite(parsed)
            && parsed >= 0)
        {
            result = parsed;
            return true;
        }

        result = 0;
        return false;
    }

    private static string ResolveTemporaryResultPath(
        string? reportedPath,
        string tempDirectory,
        string fileStem)
    {
        if (!string.IsNullOrWhiteSpace(reportedPath))
        {
            string normalizedPath = reportedPath.Trim('"');
            if (!Path.IsPathRooted(normalizedPath))
            {
                normalizedPath = Path.GetFullPath(normalizedPath, tempDirectory);
            }

            if (File.Exists(normalizedPath))
            {
                return normalizedPath;
            }
        }

        string? found = Directory.Exists(tempDirectory)
            ? Directory.EnumerateFiles(tempDirectory, fileStem + ".*")
                .Where(path => !path.EndsWith(".part", StringComparison.OrdinalIgnoreCase)
                    && !path.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;

        return found ?? throw new InvalidOperationException(
            "yt-dlp báo thành công nhưng không tìm thấy file kết quả.");
    }

    private readonly record struct YtDlpProgressSnapshot(
        long DownloadedBytes,
        long ExactTotalBytes,
        long EstimatedTotalBytes,
        double SpeedBps);

    private sealed class StableTotalSizeTracker
    {
        private const int EstimateWarmupSampleCount = 4;

        private int _estimateSampleCount;
        private long _largestWarmupEstimate;
        private long _lockedEstimatedTotalBytes;
        private long _lockedExactTotalBytes;

        public long GetStableTotalBytes(YtDlpProgressSnapshot progress)
        {
            if (_lockedExactTotalBytes > 0)
            {
                return _lockedExactTotalBytes;
            }

            if (progress.ExactTotalBytes > 0)
            {
                _lockedExactTotalBytes = Math.Max(
                    progress.ExactTotalBytes,
                    progress.DownloadedBytes);

                return _lockedExactTotalBytes;
            }

            if (_lockedEstimatedTotalBytes > 0)
            {
                return _lockedEstimatedTotalBytes;
            }

            if (progress.EstimatedTotalBytes <= 0)
            {
                return 0;
            }

            _estimateSampleCount++;
            _largestWarmupEstimate = Math.Max(
                _largestWarmupEstimate,
                Math.Max(progress.EstimatedTotalBytes, progress.DownloadedBytes));

            if (_estimateSampleCount >= EstimateWarmupSampleCount)
            {
                _lockedEstimatedTotalBytes = _largestWarmupEstimate;
            }

            return _lockedEstimatedTotalBytes;
        }
    }

    private sealed record YtDlpProcessResult(
        string? DestinationPath,
        string StandardError,
        int ExitCode);
}
