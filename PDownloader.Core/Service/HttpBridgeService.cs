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

using System.Security.Cryptography;

namespace PDownloader.Core.Service;

public sealed class HttpBridgeService : IDisposable
{
    private const string Prefix = "http://localhost:6287/";
    private const string AllowedExtensionOrigin =
        "chrome-extension://nliblbkhgljcpdboininiepogjaegien";

    private const string ClientHeaderName = "X-PDownloader-Client";
    private const string ClientHeaderValue = "browser-extension";
    private const string TokenHeaderName = "X-PDownloader-Token";

    private const int MaxRequestBodyBytes = 1024 * 1024;
    private const int MaxForwardedHeaderValueLength = 64 * 1024;
    private const int MaxFileNameLength = 180;

    private static readonly HashSet<string> AllowedForwardedHeaders =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Cookie",
            "Referer",
            "User-Agent",
            "Authorization",
            "Origin",
            "Accept",
            "Accept-Language",
        };

    private readonly HttpListener _bridgeListener = new();
    private readonly string _sessionToken = CreateSessionToken();
    private CancellationTokenSource? _cts;

    public void Start()
    {
        _bridgeListener.Prefixes.Add(Prefix);
        _bridgeListener.Start();
        _cts = new CancellationTokenSource();
        _ = AcceptLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _bridgeListener.Stop(); } catch { }
    }

    public void Dispose()
    {
        Stop();
        _bridgeListener.Close();
        _cts?.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                HttpListenerContext context = await _bridgeListener.GetContextAsync();
                _ = HandleAsync(context, ct);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"[HTTP Bridge] Accept error: {exception.Message}");
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken ct)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;
        ApplyCommonSecurityHeaders(response);

        try
        {
            if (!IsLoopbackRequest(request))
            {
                await Json(response, new { ok = false, error = "Forbidden." }, 403);
                return;
            }

            string path = NormalizePath(request.Url?.AbsolutePath);

            if (request.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                HandlePreflight(request, response, path);
                return;
            }

            string? origin = request.Headers["Origin"];
            if (!IsAllowedCallerOrigin(origin))
            {
                await Json(response, new { ok = false, error = "Forbidden origin." }, 403);
                return;
            }

            if (!string.IsNullOrWhiteSpace(origin))
            {
                ApplyCorsHeaders(response, origin);
            }

            if (!IsExpectedClient(request))
            {
                await Json(response, new { ok = false, error = "Unauthorized client." }, 401);
                return;
            }

            if (path == "/ping")
            {
                if (!request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
                {
                    await MethodNotAllowed(response, "GET");
                    return;
                }

                await Json(response, new
                {
                    ok = true,
                    app = "PDownloader",
                    version = "1.0",
                    token = _sessionToken,
                });
                return;
            }

            if (!HasValidSessionToken(request))
            {
                await Json(response, new { ok = false, error = "Invalid or expired session token." }, 401);
                return;
            }

            switch (path)
            {
                case "/download":
                    await HandleDownload(request, response, ct);
                    break;

                case "/youtube/analyze":
                    await HandleYouTubeAnalyze(request, response, ct);
                    break;

                case "/youtube/download":
                    await HandleYouTubeDownload(request, response, ct);
                    break;

                default:
                    await Json(response, new { ok = false, error = "Not found." }, 404);
                    break;
            }
        }
        catch (BridgeRequestException exception)
        {
            try
            {
                await Json(response, new { ok = false, error = exception.Message }, exception.StatusCode);
            }
            catch
            {
                SafeClose(response);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            SafeClose(response);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[HTTP Bridge] Request failed: {exception}");
            try
            {
                await Json(response, new { ok = false, error = "Internal server error." }, 500);
            }
            catch
            {
                SafeClose(response);
            }
        }
    }

    private async Task HandleDownload(
        HttpListenerRequest request,
        HttpListenerResponse response,
        CancellationToken ct)
    {
        EnsureJsonPost(request);
        DownloadPayload payload = await ReadJsonAsync<DownloadPayload>(request, ct);

        string url = ValidateHttpUrl(payload.Url);
        string fileName = SanitizeBridgeFileName(payload.FileName);
        Dictionary<string, string>? customHeaders = SanitizeForwardedHeaders(payload.Headers);

        string id = Guid.NewGuid().ToString();
        var data = new FileTask
        {
            id = id,
            url = url,

            // Browser requests are never allowed to choose an arbitrary local folder.
            // Use the application's configured/default download folder instead.
            saveTo = GetBridgeDownloadFolder(),
            fileName = fileName,
            headers = customHeaders,
        };

        Utils.AppRuntime.EnsureRunnerStarted(id, data);
        await Json(response, new { ok = true });
    }

    private static async Task HandleYouTubeAnalyze(
        HttpListenerRequest request,
        HttpListenerResponse response,
        CancellationToken ct)
    {
        EnsureJsonPost(request);
        YouTubeAnalyzePayload payload = await ReadJsonAsync<YouTubeAnalyzePayload>(request, ct);

        string url = ValidateHttpUrl(payload.Url);
        Dictionary<string, string>? headers = SanitizeForwardedHeaders(payload.Headers);
        string? cookieHeader = DownloadPathService.GetHeader(headers, "Cookie");

        YtAnalyzeResult result = await YtDlpService.Instance.AnalyzeAsync(
            url,
            cookieHeader,
            ct: ct);

        await Json(response, result);
    }

    private async Task HandleYouTubeDownload(
        HttpListenerRequest request,
        HttpListenerResponse response,
        CancellationToken ct)
    {
        EnsureJsonPost(request);
        YoutubePayload payload = await ReadJsonAsync<YoutubePayload>(request, ct);

        string url = ValidateHttpUrl(payload.Url);
        string fileName = SanitizeBridgeFileName(payload.Filename);
        Dictionary<string, string>? headers = SanitizeForwardedHeaders(payload.Headers);
        string formatId = string.IsNullOrWhiteSpace(payload.FormatId)
            ? "bestvideo+bestaudio/best"
            : payload.FormatId.Trim();

        if (formatId.Length > 512 || ContainsControlCharacters(formatId))
        {
            throw new BridgeRequestException(400, "Invalid formatId.");
        }

        string id = Guid.NewGuid().ToString();
        CFSCommandHandler.RegisterYoutubePending(id, formatId);

        var data = new FileTask
        {
            id = id,
            url = url,
            formatId = formatId,
            saveTo = GetBridgeDownloadFolder(),
            fileName = fileName,
            title = SanitizeText(payload.Title, 500),
            filesize = Math.Max(0, payload.Filesize),
            headers = headers,
        };

        Utils.AppRuntime.EnsureRunnerStarted(id, data);
        await Json(response, new { success = true });
    }

    private static void HandlePreflight(
        HttpListenerRequest request,
        HttpListenerResponse response,
        string path)
    {
        string? origin = request.Headers["Origin"];
        if (!IsAllowedOrigin(origin))
        {
            response.StatusCode = 403;
            SafeClose(response);
            return;
        }

        string? requestedMethod = request.Headers["Access-Control-Request-Method"];
        string expectedMethod = path == "/ping" ? "GET" : "POST";

        if (!IsKnownPath(path)
            || !string.Equals(requestedMethod, expectedMethod, StringComparison.OrdinalIgnoreCase))
        {
            response.StatusCode = 405;
            SafeClose(response);
            return;
        }

        if (!AreRequestedHeadersAllowed(request.Headers["Access-Control-Request-Headers"]))
        {
            response.StatusCode = 403;
            SafeClose(response);
            return;
        }

        ApplyCorsHeaders(response, origin!);
        response.StatusCode = 204;
        SafeClose(response);
    }

    private static bool AreRequestedHeadersAllowed(string? requestedHeaders)
    {
        if (string.IsNullOrWhiteSpace(requestedHeaders))
        {
            return true;
        }

        foreach (string header in requestedHeaders.Split(','))
        {
            string normalized = header.Trim();
            if (!normalized.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)
                && !normalized.Equals(ClientHeaderName, StringComparison.OrdinalIgnoreCase)
                && !normalized.Equals(TokenHeaderName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static void EnsureJsonPost(HttpListenerRequest request)
    {
        if (!request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            throw new BridgeRequestException(405, "Method not allowed.");
        }

        string? contentType = request.ContentType;
        if (string.IsNullOrWhiteSpace(contentType)
            || !contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            throw new BridgeRequestException(415, "Content-Type must be application/json.");
        }
    }

    private static async Task<T> ReadJsonAsync<T>(
        HttpListenerRequest request,
        CancellationToken ct)
    {
        string body = await ReadBodyAsync(request, ct);
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new BridgeRequestException(400, "Request body is required.");
        }

        try
        {
            T? payload = JsonSerializer.Deserialize<T>(body);
            return payload ?? throw new BridgeRequestException(400, "Invalid JSON body.");
        }
        catch (JsonException)
        {
            throw new BridgeRequestException(400, "Invalid JSON body.");
        }
    }

    private static async Task<string> ReadBodyAsync(
        HttpListenerRequest request,
        CancellationToken ct)
    {
        long contentLength = request.ContentLength64;
        if (contentLength > MaxRequestBodyBytes)
        {
            throw new BridgeRequestException(413, "Request body is too large.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

        using var memoryStream = contentLength > 0
            ? new MemoryStream((int)contentLength)
            : new MemoryStream();

        byte[] buffer = new byte[8192];
        int totalRead = 0;

        try
        {
            while (true)
            {
                int read = await request.InputStream.ReadAsync(buffer.AsMemory(), timeoutCts.Token);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
                if (totalRead > MaxRequestBodyBytes)
                {
                    throw new BridgeRequestException(413, "Request body is too large.");
                }

                memoryStream.Write(buffer, 0, read);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new BridgeRequestException(408, "Request body timed out.");
        }

        return Encoding.UTF8.GetString(memoryStream.GetBuffer(), 0, totalRead);
    }

    private bool HasValidSessionToken(HttpListenerRequest request)
    {
        string? suppliedToken = request.Headers[TokenHeaderName];
        if (string.IsNullOrWhiteSpace(suppliedToken))
        {
            return false;
        }

        byte[] expected = Encoding.UTF8.GetBytes(_sessionToken);
        byte[] supplied = Encoding.UTF8.GetBytes(suppliedToken);

        return expected.Length == supplied.Length
            && CryptographicOperations.FixedTimeEquals(expected, supplied);
    }

    private static bool IsExpectedClient(HttpListenerRequest request)
    {
        return string.Equals(
            request.Headers[ClientHeaderName],
            ClientHeaderValue,
            StringComparison.Ordinal);
    }

    private static bool IsLoopbackRequest(HttpListenerRequest request)
    {
        IPAddress? remoteAddress = request.RemoteEndPoint?.Address;
        return remoteAddress != null && IPAddress.IsLoopback(remoteAddress);
    }

    private static bool IsAllowedOrigin(string? origin)
    {
        if (string.Equals(
            origin,
            AllowedExtensionOrigin,
            StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

#if DEBUG
        // Unpacked Chromium extensions can have a different generated ID while
        // developing. Keep release builds locked to the signed extension ID.
        return Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri)
            && uri.Scheme.Equals("chrome-extension", StringComparison.OrdinalIgnoreCase);
#else
        return false;
#endif
    }

    private static bool IsAllowedCallerOrigin(string? origin)
    {
        // Chromium extensions with host permissions may omit Origin entirely.
        // Normal web pages send their own Origin and cannot spoof the custom
        // client header without first passing the CORS preflight below.
        return string.IsNullOrWhiteSpace(origin) || IsAllowedOrigin(origin);
    }

    private static bool IsKnownPath(string path)
    {
        return path is "/ping" or "/download" or "/youtube/analyze" or "/youtube/download";
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            return "/";
        }

        return path.TrimEnd('/').ToLowerInvariant();
    }

    private static string GetBridgeDownloadFolder()
    {
        string? configuredFolder =
            CFSCommandHandler.DownloadConfigService.DownloadConfigs?.DefaultDownloadFolder;

        if (!string.IsNullOrWhiteSpace(configuredFolder)
            && Directory.Exists(configuredFolder))
        {
            return configuredFolder;
        }

        string fallbackFolder = Helpers.GetDefaultFolder();
        if (!string.IsNullOrWhiteSpace(fallbackFolder))
        {
            Directory.CreateDirectory(fallbackFolder);
            return fallbackFolder;
        }

        throw new BridgeRequestException(500, "No valid download folder is available.");
    }

    private static string ValidateHttpUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 16 * 1024
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new BridgeRequestException(400, "A valid http/https url is required.");
        }

        return value.Trim();
    }

    private static string SanitizeBridgeFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string leafName;
        try
        {
            leafName = Path.GetFileName(value.Trim());
        }
        catch
        {
            leafName = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(leafName))
        {
            return string.Empty;
        }

        string safeName = DownloadPathService.SanitizeFileName(leafName);
        if (safeName.Length <= MaxFileNameLength)
        {
            return safeName;
        }

        string extension = Path.GetExtension(safeName);
        int stemLength = Math.Max(1, MaxFileNameLength - extension.Length);
        string stem = Path.GetFileNameWithoutExtension(safeName);
        if (stem.Length > stemLength)
        {
            stem = stem[..stemLength];
        }

        return stem + extension;
    }

    private static Dictionary<string, string>? SanitizeForwardedHeaders(
        Dictionary<string, string>? headers)
    {
        if (headers is not { Count: > 0 })
        {
            return null;
        }

        var sanitized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string value) in headers)
        {
            if (!AllowedForwardedHeaders.Contains(key)
                || string.IsNullOrWhiteSpace(value)
                || value.Length > MaxForwardedHeaderValueLength
                || ContainsControlCharacters(value, allowTab: true))
            {
                continue;
            }

            sanitized[key] = value.Trim();
        }

        return sanitized.Count == 0 ? null : sanitized;
    }

    private static string SanitizeText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string sanitized = new(value
            .Where(character => !char.IsControl(character) || character is '\t' or '\r' or '\n')
            .ToArray());

        sanitized = sanitized.Trim();
        return sanitized.Length <= maxLength ? sanitized : sanitized[..maxLength];
    }

    private static bool ContainsControlCharacters(string value, bool allowTab = false)
    {
        foreach (char character in value)
        {
            if (character == '\r' || character == '\n' || character == '\0')
            {
                return true;
            }

            if (char.IsControl(character) && !(allowTab && character == '\t'))
            {
                return true;
            }
        }

        return false;
    }

    private static string CreateSessionToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void ApplyCommonSecurityHeaders(HttpListenerResponse response)
    {
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["Pragma"] = "no-cache";
        response.Headers["X-Content-Type-Options"] = "nosniff";
    }

    private static void ApplyCorsHeaders(HttpListenerResponse response, string origin)
    {
        response.Headers["Access-Control-Allow-Origin"] = origin;
        response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
        response.Headers["Access-Control-Allow-Headers"] =
            $"Content-Type, {ClientHeaderName}, {TokenHeaderName}";
        response.Headers["Access-Control-Max-Age"] = "600";
        response.Headers["Vary"] = "Origin";
    }

    private static async Task MethodNotAllowed(
        HttpListenerResponse response,
        string allowedMethod)
    {
        response.Headers["Allow"] = allowedMethod;
        await Json(response, new { ok = false, error = "Method not allowed." }, 405);
    }

    private static async Task Json(
        HttpListenerResponse response,
        object value,
        int status = 200)
    {
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(value);
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = data.Length;
        response.StatusCode = status;
        await response.OutputStream.WriteAsync(data);
        SafeClose(response);
    }

    private static void SafeClose(HttpListenerResponse response)
    {
        try { response.Close(); } catch { }
    }

    private sealed class BridgeRequestException : Exception
    {
        public BridgeRequestException(int statusCode, string message)
            : base(message)
        {
            StatusCode = statusCode;
        }

        public int StatusCode { get; }
    }

    private sealed class DownloadPayload
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        // Retained only for wire compatibility with older extension builds.
        // The bridge intentionally ignores this value.
        [JsonPropertyName("saveTo")]
        public string? SaveTo { get; set; }

        [JsonPropertyName("fileName")]
        public string? FileName { get; set; }

        [JsonPropertyName("headers")]
        public Dictionary<string, string>? Headers { get; set; }
    }

    private sealed class YouTubeAnalyzePayload
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("headers")]
        public Dictionary<string, string>? Headers { get; set; }
    }

    private sealed class YoutubePayload
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("formatId")]
        public string? FormatId { get; set; }

        [JsonPropertyName("filename")]
        public string? Filename { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("filesize")]
        public long Filesize { get; set; }

        [JsonPropertyName("headers")]
        public Dictionary<string, string>? Headers { get; set; }
    }
}
