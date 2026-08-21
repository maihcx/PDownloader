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

internal static class YtDlpCommandBuilder
{
    public static IReadOnlyList<string> BuildResolveDirectUrls(
        string pageUrl,
        string formatId,
        string? referer,
        string? userAgent,
        IReadOnlyDictionary<string, string>? extraHeaders,
        string quickJsPath,
        string? cookieFile)
    {
        var arguments = new List<string>
        {
            "-f",
            formatId,
            "-j",
            "--no-warnings",
            "--no-playlist",
        };

        AddReferer(arguments, referer);
        AddUserAgent(arguments, userAgent);
        AddExtraHeaders(arguments, extraHeaders);
        AddQuickJs(arguments, quickJsPath);
        AddCookieFile(arguments, cookieFile);
        AddUrl(arguments, pageUrl);
        return arguments;
    }

    public static IReadOnlyList<string> BuildAnalyze(
        string url,
        string? referer,
        string? userAgent,
        IReadOnlyDictionary<string, string>? extraHeaders,
        string quickJsPath,
        string? cookieFile)
    {
        var arguments = new List<string>
        {
            "-J",
            "--no-warnings",
            "--no-playlist",
        };

        AddReferer(arguments, referer);
        AddUserAgent(arguments, userAgent);
        AddExtraHeaders(arguments, extraHeaders);
        AddQuickJs(arguments, quickJsPath);
        AddCookieFile(arguments, cookieFile);
        AddUrl(arguments, url);
        return arguments;
    }

    public static IReadOnlyList<string> BuildResolveHlsFragments(
        string url,
        string? formatId,
        string? referer,
        string? userAgent,
        IReadOnlyDictionary<string, string>? extraHeaders,
        string quickJsPath,
        string? cookieFile)
    {
        var arguments = new List<string>
        {
            "-j",
            "--no-warnings",
            "--no-playlist",
        };

        AddFormat(arguments, formatId);

        AddReferer(arguments, referer);
        AddUserAgent(arguments, userAgent);
        AddExtraHeaders(arguments, extraHeaders);
        AddQuickJs(arguments, quickJsPath);
        AddCookieFile(arguments, cookieFile);
        AddUrl(arguments, url);
        return arguments;
    }

    private static void AddFormat(List<string> arguments, string? formatId)
    {
        if (string.IsNullOrWhiteSpace(formatId))
        {
            return;
        }

        arguments.Add("-f");
        arguments.Add(formatId);
    }

    private static void AddReferer(List<string> arguments, string? referer)
    {
        if (string.IsNullOrWhiteSpace(referer))
        {
            return;
        }

        arguments.Add("--referer");
        arguments.Add(referer);
    }

    private static void AddUserAgent(List<string> arguments, string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return;
        }

        arguments.Add("--user-agent");
        arguments.Add(userAgent);
    }

    private static void AddExtraHeaders(
        List<string> arguments,
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

            arguments.Add("--add-header");
            arguments.Add($"{name}:{value}");
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

    private static void AddQuickJs(List<string> arguments, string quickJsPath)
    {
        arguments.Add("--js-runtimes");
        arguments.Add($"quickjs:{quickJsPath}");
    }

    private static void AddCookieFile(List<string> arguments, string? cookieFile)
    {
        if (string.IsNullOrWhiteSpace(cookieFile))
        {
            return;
        }

        arguments.Add("--cookies");
        arguments.Add(cookieFile);
    }

    private static void AddUrl(List<string> arguments, string url)
    {
        arguments.Add("--");
        arguments.Add(url);
    }
}
