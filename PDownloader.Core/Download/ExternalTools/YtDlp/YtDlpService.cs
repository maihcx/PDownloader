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

namespace PDownloader.Core.Download.ExternalTools.YtDlp;

public sealed class YtDlpService
{
    public static YtDlpService Instance { get; } = new();

    private readonly YtDlpExecutableLocator _executableLocator;
    private readonly YtDlpCookieFileService _cookieFileService;
    private readonly ExternalProcessRunner _processRunner;

    private YtDlpService()
        : this(
            YtDlpExecutableLocator.Instance,
            YtDlpCookieFileService.Instance,
            new ExternalProcessRunner())
    {
    }

    internal YtDlpService(
        YtDlpExecutableLocator executableLocator,
        YtDlpCookieFileService cookieFileService,
        ExternalProcessRunner processRunner)
    {
        _executableLocator = executableLocator;
        _cookieFileService = cookieFileService;
        _processRunner = processRunner;
    }

    public string? FindYtDlp() => _executableLocator.FindYtDlp();

    public string? FindQJS() => _executableLocator.FindQuickJs();

    public async Task<List<ResolvedStream>> ResolveDirectUrlsAsync(
        string pageUrl,
        string formatId,
        string? referer,
        string? cookieHeader = null,
        string? cookieJarJson = null,
        string? userAgent = null,
        IReadOnlyDictionary<string, string>? extraHeaders = null,
        CancellationToken ct = default)
    {
        string effectivePageUrl = VimeoUrlNormalizer.Normalize(pageUrl, referer);
        string effectiveFormatId = YtDlpFormatSelector.Normalize(pageUrl, formatId);
        string ytDlpPath = RequireYtDlp();
        string quickJsPath = RequireQuickJs();
        string? cookieFile = _cookieFileService.Create(
            cookieHeader,
            effectivePageUrl,
            cookieJarJson);

        try
        {
            IReadOnlyList<string> arguments = YtDlpCommandBuilder.BuildResolveDirectUrls(
                effectivePageUrl,
                effectiveFormatId,
                referer,
                userAgent,
                extraHeaders,
                quickJsPath,
                cookieFile);
            ExternalProcessResult result = await _processRunner.RunAsync(
                ytDlpPath,
                arguments,
                ct);

            EnsureSuccessful(result);
            List<ResolvedStream> streams =
                YtDlpJsonParser.ParseResolvedStreams(result.StandardOutput);
            EnsureDirectHttpStreams(streams);
            return streams;
        }
        finally
        {
            _cookieFileService.DeleteSafe(cookieFile);
        }
    }

    public async Task<YtAnalyzeResult> AnalyzeAsync(
        string url,
        string? cookieHeader = null,
        string? cookieJarJson = null,
        string? userAgent = null,
        IReadOnlyDictionary<string, string>? extraHeaders = null,
        CancellationToken ct = default)
    {
        string? ytDlpPath = FindYtDlp();
        if (ytDlpPath == null)
        {
            return YtAnalyzeResult.Fail(
                "yt-dlp không tìm thấy. Đặt yt-dlp.exe cạnh PDownloader.exe " +
                "hoặc thêm vào PATH rồi khởi động lại.");
        }

        string? referer = GetHeader(extraHeaders, "Referer");
        string effectiveUrl = VimeoUrlNormalizer.Normalize(url, referer);
        string quickJsPath = RequireQuickJs();
        string? cookieFile = _cookieFileService.Create(
            cookieHeader,
            effectiveUrl,
            cookieJarJson);

        try
        {
            IReadOnlyList<string> arguments = YtDlpCommandBuilder.BuildAnalyze(
                effectiveUrl,
                referer,
                userAgent,
                extraHeaders,
                quickJsPath,
                cookieFile);
            ExternalProcessResult result = await _processRunner.RunAsync(
                ytDlpPath,
                arguments,
                ct);

            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                return YtAnalyzeResult.Fail(YtDlpErrorParser.Parse(result.StandardError));
            }

            try
            {
                return YtDlpJsonParser.ParseAnalysis(result.StandardOutput);
            }
            catch (Exception exception)
            {
                return YtAnalyzeResult.Fail(
                    $"Lỗi parse JSON từ yt-dlp: {exception.Message}");
            }
        }
        finally
        {
            _cookieFileService.DeleteSafe(cookieFile);
        }
    }

    public async Task<HlsFragmentsResult?> ResolveHlsFragmentsAsync(
        string url,
        string? referer,
        string? cookieHeader,
        string? cookieJarJson,
        string? userAgent,
        IReadOnlyDictionary<string, string>? extraHeaders,
        CancellationToken ct = default)
    {
        string effectiveUrl = VimeoUrlNormalizer.Normalize(url, referer);
        string ytDlpPath = RequireYtDlp();
        string quickJsPath = RequireQuickJs();
        string? cookieFile = _cookieFileService.Create(
            cookieHeader,
            effectiveUrl,
            cookieJarJson);

        try
        {
            IReadOnlyList<string> arguments = YtDlpCommandBuilder.BuildResolveHlsFragments(
                effectiveUrl,
                referer,
                userAgent,
                extraHeaders,
                quickJsPath,
                cookieFile);
            ExternalProcessResult result = await _processRunner.RunAsync(
                ytDlpPath,
                arguments,
                ct);

            EnsureSuccessful(result);
            return YtDlpJsonParser.ParseHlsFragments(result.StandardOutput);
        }
        finally
        {
            _cookieFileService.DeleteSafe(cookieFile);
        }
    }

    private static void EnsureDirectHttpStreams(
        IReadOnlyCollection<ResolvedStream> streams)
    {
        ResolvedStream? unsupported = streams.FirstOrDefault(stream => !stream.IsDirectHttp);
        if (unsupported == null)
        {
            return;
        }

        string format = string.IsNullOrWhiteSpace(unsupported.FormatId)
            ? "unknown"
            : unsupported.FormatId;
        string protocol = string.IsNullOrWhiteSpace(unsupported.Protocol)
            ? "unknown"
            : unsupported.Protocol;

        throw new InvalidOperationException(
            $"yt-dlp selected a manifest/fragmented stream " +
            $"(format {format}, protocol {protocol}) instead of a complete HTTP file. " +
            "PDownloader cannot add this URL to the standard segment downloader.");
    }

    private static string? GetHeader(
        IReadOnlyDictionary<string, string>? headers,
        string name)
    {
        if (headers == null)
        {
            return null;
        }

        foreach ((string key, string value) in headers)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    private string RequireYtDlp()
    {
        return FindYtDlp()
            ?? throw new InvalidOperationException("yt-dlp không tìm thấy.");
    }

    private string RequireQuickJs()
    {
        return FindQJS()
            ?? throw new InvalidOperationException("qjs không tìm thấy.");
    }

    private static void EnsureSuccessful(ExternalProcessResult result)
    {
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            throw new InvalidOperationException(
                YtDlpErrorParser.Parse(result.StandardError));
        }
    }
}
