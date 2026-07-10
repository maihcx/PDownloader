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
        CancellationToken ct = default)
    {
        string ytDlpPath = RequireYtDlp();
        string quickJsPath = RequireQuickJs();
        string? cookieFile = _cookieFileService.Create(cookieHeader);

        try
        {
            IReadOnlyList<string> arguments = YtDlpCommandBuilder.BuildResolveDirectUrls(
                pageUrl,
                formatId,
                referer,
                quickJsPath,
                cookieFile);
            ExternalProcessResult result = await _processRunner.RunAsync(
                ytDlpPath,
                arguments,
                ct);

            EnsureSuccessful(result);
            return YtDlpJsonParser.ParseResolvedStreams(result.StandardOutput);
        }
        finally
        {
            _cookieFileService.DeleteSafe(cookieFile);
        }
    }

    public async Task<YtAnalyzeResult> AnalyzeAsync(
        string url,
        string? cookieHeader = null,
        CancellationToken ct = default)
    {
        string? ytDlpPath = FindYtDlp();
        if (ytDlpPath == null)
        {
            return YtAnalyzeResult.Fail(
                "yt-dlp không tìm thấy. Đặt yt-dlp.exe cạnh PDownloader.exe " +
                "hoặc thêm vào PATH rồi khởi động lại.");
        }

        string quickJsPath = RequireQuickJs();
        string? cookieFile = _cookieFileService.Create(cookieHeader);

        try
        {
            IReadOnlyList<string> arguments = YtDlpCommandBuilder.BuildAnalyze(
                url,
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
        CancellationToken ct = default)
    {
        string ytDlpPath = RequireYtDlp();
        string quickJsPath = RequireQuickJs();
        string? cookieFile = _cookieFileService.Create(cookieHeader);

        try
        {
            IReadOnlyList<string> arguments = YtDlpCommandBuilder.BuildResolveHlsFragments(
                url,
                referer,
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
