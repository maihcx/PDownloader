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

namespace PDownloader.Core.Download.ExternalTools.YtDlp;

internal sealed class YtDlpCookieFileService
{
    public static YtDlpCookieFileService Instance { get; } = new();

    private const string CookieExpiry = "2147483647";

    private YtDlpCookieFileService()
    {
    }

    public string? Create(
        string? cookieHeader,
        string sourceUrl,
        string? cookieJarJson = null)
    {
        var content = new StringBuilder();
        content.AppendLine("# Netscape HTTP Cookie File");

        int cookieCount = AppendStructuredCookies(content, cookieJarJson);
        if (cookieCount == 0)
        {
            cookieCount = AppendCookieHeader(content, cookieHeader, sourceUrl);
        }

        if (cookieCount == 0)
        {
            return null;
        }

        string path = Path.Combine(
            Path.GetTempPath(),
            $"pdownloader_cookies_{Guid.NewGuid():N}.txt");

        File.WriteAllText(
            path,
            content.ToString(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return path;
    }

    public void DeleteSafe(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private static int AppendStructuredCookies(
        StringBuilder content,
        string? cookieJarJson)
    {
        if (string.IsNullOrWhiteSpace(cookieJarJson))
        {
            return 0;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(cookieJarJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return 0;
            }

            var selected = new Dictionary<string, StructuredCookie>(StringComparer.Ordinal);
            var insertionOrder = new List<string>();

            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string name = GetString(element, "name");
                string value = GetString(element, "value");
                string domain = GetString(element, "domain");
                string path = GetString(element, "path");
                bool secure = GetBoolean(element, "secure");
                bool httpOnly = GetBoolean(element, "httpOnly");
                bool hostOnly = GetBoolean(element, "hostOnly");
                bool session = GetBoolean(element, "session");

                if (string.IsNullOrWhiteSpace(name)
                    || string.IsNullOrWhiteSpace(domain)
                    || HasInvalidCookieField(name)
                    || HasInvalidCookieField(value)
                    || HasInvalidCookieField(domain)
                    || HasInvalidCookieField(path))
                {
                    continue;
                }

                domain = domain.Trim().ToLowerInvariant();
                if (hostOnly)
                {
                    domain = domain.TrimStart('.');
                }
                else if (!domain.StartsWith('.'))
                {
                    domain = "." + domain;
                }

                path = string.IsNullOrWhiteSpace(path) ? "/" : path.Trim();
                name = name.Trim();

                string netscapeIdentity = $"{domain}\n{path}\n{name}";
                var candidate = new StructuredCookie(
                    Domain: httpOnly ? "#HttpOnly_" + domain : domain,
                    IncludeSubdomains: !hostOnly,
                    Path: path,
                    Secure: secure,
                    Expiry: session ? "0" : GetExpiration(element),
                    Name: name,
                    Value: value,
                    ContextSpecificity: GetContextSpecificity(element));

                if (!selected.TryGetValue(netscapeIdentity, out StructuredCookie? existing))
                {
                    selected[netscapeIdentity] = candidate;
                    insertionOrder.Add(netscapeIdentity);
                    continue;
                }

                if (candidate.ContextSpecificity > existing.ContextSpecificity)
                {
                    selected[netscapeIdentity] = candidate;
                }
            }

            foreach (string identity in insertionOrder)
            {
                StructuredCookie cookie = selected[identity];
                AppendCookieLine(
                    content,
                    cookie.Domain,
                    cookie.IncludeSubdomains,
                    cookie.Path,
                    cookie.Secure,
                    cookie.Expiry,
                    cookie.Name,
                    cookie.Value);
            }

            return selected.Count;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static int AppendCookieHeader(
        StringBuilder content,
        string? cookieHeader,
        string sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(cookieHeader))
        {
            return 0;
        }

        CookieScope scope = ResolveCookieScope(sourceUrl);
        int count = 0;

        foreach (string pair in cookieHeader.Split(
            ';',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            int separatorIndex = pair.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            string name = pair[..separatorIndex].Trim();
            string value = pair[(separatorIndex + 1)..].Trim();
            if (name.Length == 0
                || HasInvalidCookieField(name)
                || HasInvalidCookieField(value))
            {
                continue;
            }

            AppendCookieLine(
                content,
                scope.Domain,
                scope.IncludeSubdomains,
                "/",
                scope.Secure,
                CookieExpiry,
                name,
                value);
            count++;
        }

        return count;
    }

    private static void AppendCookieLine(
        StringBuilder content,
        string domain,
        bool includeSubdomains,
        string path,
        bool secure,
        string expiry,
        string name,
        string value)
    {
        content.Append(domain).Append('\t')
            .Append(includeSubdomains ? "TRUE" : "FALSE").Append('\t')
            .Append(path).Append('\t')
            .Append(secure ? "TRUE" : "FALSE").Append('\t')
            .Append(expiry).Append('\t')
            .Append(name).Append('\t')
            .Append(value).Append('\n');
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
    }

    private static bool GetBoolean(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            && value.GetBoolean();
    }

    private static int GetContextSpecificity(JsonElement element)
    {
        int score = 0;

        if (!string.IsNullOrWhiteSpace(GetString(element, "firstPartyDomain")))
        {
            score += 2;
        }

        if (element.TryGetProperty("partitionKey", out JsonElement partitionKey)
            && partitionKey.ValueKind == JsonValueKind.Object)
        {
            if (!string.IsNullOrWhiteSpace(GetString(partitionKey, "topLevelSite")))
            {
                score += 4;
            }

            if (partitionKey.TryGetProperty(
                    "hasCrossSiteAncestor",
                    out JsonElement crossSiteAncestor)
                && crossSiteAncestor.ValueKind == JsonValueKind.True)
            {
                score += 1;
            }
        }

        return score;
    }

    private static string GetExpiration(JsonElement element)
    {
        if (element.TryGetProperty("expirationDate", out JsonElement expiration)
            && expiration.ValueKind == JsonValueKind.Number
            && expiration.TryGetDouble(out double seconds)
            && double.IsFinite(seconds)
            && seconds > 0)
        {
            return Math.Floor(seconds)
                .ToString(CultureInfo.InvariantCulture);
        }

        return "0";
    }

    private static bool HasInvalidCookieField(string value) =>
        value.IndexOfAny(['\t', '\r', '\n', '\0']) >= 0;

    private static CookieScope ResolveCookieScope(string sourceUrl)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out Uri? uri)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ArgumentException("URL cookie không hợp lệ.", nameof(sourceUrl));
        }

        string host = uri.IdnHost.ToLowerInvariant();
        bool secure = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);

        string? sharedDomain = host switch
        {
            "instagram.com" => ".instagram.com",
            "tiktok.com" => ".tiktok.com",
            "facebook.com" => ".facebook.com",
            "x.com" => ".x.com",
            "twitter.com" => ".twitter.com",
            _ when host.EndsWith(".instagram.com", StringComparison.OrdinalIgnoreCase) => ".instagram.com",
            _ when host.EndsWith(".tiktok.com", StringComparison.OrdinalIgnoreCase) => ".tiktok.com",
            _ when host.EndsWith(".facebook.com", StringComparison.OrdinalIgnoreCase) => ".facebook.com",
            _ when host.EndsWith(".x.com", StringComparison.OrdinalIgnoreCase) => ".x.com",
            _ when host.EndsWith(".twitter.com", StringComparison.OrdinalIgnoreCase) => ".twitter.com",
            _ => null,
        };

        return sharedDomain != null
            ? new CookieScope(sharedDomain, IncludeSubdomains: true, Secure: secure)
            : new CookieScope(host, IncludeSubdomains: false, Secure: secure);
    }

    private sealed record StructuredCookie(
        string Domain,
        bool IncludeSubdomains,
        string Path,
        bool Secure,
        string Expiry,
        string Name,
        string Value,
        int ContextSpecificity);

    private readonly record struct CookieScope(
        string Domain,
        bool IncludeSubdomains,
        bool Secure);
}
