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
using System.Text.RegularExpressions;

namespace PDownloader.Core.Download.Hls;

internal sealed class YtDlpHlsDownloadService
{
    private static readonly Regex ProgressRegex = new(
        @"^\[download\]\s+(?<pct>[\d.]+)%\s+of\s+~?\s*(?<size>[\d.]+)(?<unit>Ki?B|Mi?B|Gi?B|B)",
        RegexOptions.Compiled);

    private static readonly Regex DestinationRegex = new(
        @"^\[(?:download|Merger)\]\s+(?:Destination:|Merging formats into)\s*""?(?<path>.+?)""?$",
        RegexOptions.Compiled);

    public async Task<string> DownloadAsync(
        string url,
        string tempDirectory,
        string outputPathWithoutExtension,
        string? referer,
        string? cookieHeader,
        int preferredFragmentCount,
        Action<long, long>? reportProgress,
        CancellationToken cancellationToken)
    {
        string ytDlpPath = YtDlpService.Instance.FindYtDlp()
            ?? throw new InvalidOperationException("yt-dlp không tìm thấy.");

        string fileStem = Path.GetFileName(outputPathWithoutExtension);
        string temporaryOutputWithoutExtension = Path.Combine(tempDirectory, fileStem);
        string? cookieFile = YtDlpService.WriteNetscapeCookieFile(cookieHeader);

        try
        {
            ProcessStartInfo startInfo = BuildStartInfo(
                ytDlpPath,
                url,
                temporaryOutputWithoutExtension,
                referer,
                cookieFile,
                preferredFragmentCount);

            YtDlpProcessResult processResult = await RunProcessAsync(
                startInfo,
                reportProgress,
                cancellationToken);

            if (processResult.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    YtDlpService.ParseYtDlpError(processResult.StandardError));
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
            YtDlpService.DeleteCookieFileSafe(cookieFile);
        }
    }

    private static ProcessStartInfo BuildStartInfo(
        string ytDlpPath,
        string url,
        string outputPathWithoutExtension,
        string? referer,
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
        startInfo.ArgumentList.Add("--merge-output-format");
        startInfo.ArgumentList.Add("mp4");
        startInfo.ArgumentList.Add("--hls-prefer-native");
        startInfo.ArgumentList.Add("--concurrent-fragments");
        startInfo.ArgumentList.Add(Math.Clamp(preferredFragmentCount, 1, 16).ToString(CultureInfo.InvariantCulture));

        if (!string.IsNullOrWhiteSpace(referer))
        {
            startInfo.ArgumentList.Add("--add-header");
            startInfo.ArgumentList.Add($"Referer:{referer}");
        }

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

    private static async Task<YtDlpProcessResult> RunProcessAsync(
        ProcessStartInfo startInfo,
        Action<long, long>? reportProgress,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        var standardError = new StringBuilder();
        string? destinationPath = null;

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data == null)
            {
                return;
            }

            Match progressMatch = ProgressRegex.Match(args.Data);
            if (progressMatch.Success)
            {
                double percent = double.Parse(
                    progressMatch.Groups["pct"].Value,
                    CultureInfo.InvariantCulture);
                double size = double.Parse(
                    progressMatch.Groups["size"].Value,
                    CultureInfo.InvariantCulture);
                long totalBytes = (long)(size * GetUnitMultiplier(
                    progressMatch.Groups["unit"].Value));
                long downloadedBytes = (long)(totalBytes * percent / 100.0);
                reportProgress?.Invoke(downloadedBytes, totalBytes);
                return;
            }

            Match destinationMatch = DestinationRegex.Match(args.Data);
            if (destinationMatch.Success)
            {
                destinationPath = destinationMatch.Groups["path"].Value.Trim();
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

    private static double GetUnitMultiplier(string unit) => unit switch
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

    private sealed record YtDlpProcessResult(
        string? DestinationPath,
        string StandardError,
        int ExitCode);
}
