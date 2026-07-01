using System.Text.Json.Serialization;

namespace PDownloader.Core.Service
{
    public sealed class HttpBridgeService : IDisposable
    {
        private const string Prefix = "http://localhost:6287/";
        private readonly HttpListener _listener = new();
        private CancellationTokenSource? _cts;

        public void Start()
        {
            _listener.Prefixes.Add(Prefix);
            _listener.Start();
            _cts = new CancellationTokenSource();
            _ = AcceptLoopAsync(_cts.Token);
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _listener.Stop(); } catch { }
        }

        public void Dispose() => Stop();

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var ctx = await _listener.GetContextAsync();
                    _ = HandleAsync(ctx, ct);
                }
                catch (HttpListenerException) when (ct.IsCancellationRequested) { break; }
                catch { }
            }
        }

        private async Task HandleAsync(HttpListenerContext ctx, CancellationToken ct)
        {
            var req = ctx.Request;
            var resp = ctx.Response;

            resp.AddHeader("Access-Control-Allow-Origin", "*");
            resp.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            resp.AddHeader("Access-Control-Allow-Headers", "Content-Type, Authorization");

            if (req.HttpMethod == "OPTIONS") { resp.StatusCode = 204; resp.Close(); return; }

            string path = req.Url?.AbsolutePath.TrimEnd('/').ToLowerInvariant() ?? "/";

            try
            {
                switch (path)
                {
                    case "/ping":
                        await Json(resp, new { ok = true, app = "PDownloader", version = "1.0" });
                        break;

                    case "/download":
                        await HandleDownload(req, resp);
                        break;

                    case "/youtube/analyze":
                        await HandleYouTubeAnalyze(req, resp, ct);
                        break;

                    case "/youtube/download":
                        await HandleYouTubeDownload(req, resp);
                        break;

                    default:
                        resp.StatusCode = 404; resp.Close();
                        break;
                }
            }
            catch (Exception ex)
            {
                try { await Json(resp, new { ok = false, error = ex.Message }, 500); }
                catch { resp.Close(); }
            }
        }

        private async Task HandleDownload(HttpListenerRequest req, HttpListenerResponse resp)
        {
            if (req.HttpMethod != "POST") { resp.StatusCode = 405; resp.Close(); return; }

            string body = await ReadBodyAsync(req);

            var payload = JsonSerializer.Deserialize<DownloadPayload>(body);

            if (payload == null || string.IsNullOrWhiteSpace(payload.Url))
            {
                await Json(resp, new { ok = false, error = "url is required" }, 400);
                return;
            }

            var id = Guid.NewGuid().ToString();

            Dictionary<string, string>? customHeaders = null;
            if (payload.Headers is { Count: > 0 })
            {
                customHeaders = payload.Headers
                    .Where(kv => !string.IsNullOrWhiteSpace(kv.Key)
                              && !string.IsNullOrWhiteSpace(kv.Value))
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
                if (customHeaders.Count == 0) customHeaders = null;
            }

            var data = new FileTask
            {
                id       = id,
                url      = payload.Url,
                saveTo   = string.IsNullOrWhiteSpace(payload.SaveTo) ? Helpers.GetDefaultFolder() : payload.SaveTo,
                fileName = payload.FileName ?? string.Empty,
                headers  = customHeaders
            };
            Utils.AppRuntime.EnsureRunnerStarted(id, data);

            await Json(resp, new { ok = true });
        }

        private static async Task HandleYouTubeAnalyze(
            HttpListenerRequest req, HttpListenerResponse resp, CancellationToken ct)
        {
            if (req.HttpMethod != "POST") { resp.StatusCode = 405; resp.Close(); return; }

            string body = await ReadBodyAsync(req);

            string? url;
            try
            {
                using var doc = JsonDocument.Parse(body);
                url = doc.RootElement.GetStringOrDefault("url");
            }
            catch
            {
                await Json(resp, new { success = false, error = "Invalid JSON body." }, 400);
                return;
            }

            if (string.IsNullOrWhiteSpace(url))
            {
                await Json(resp, new { success = false, error = "url is required." }, 400);
                return;
            }

            var result = await YtDlpService.Instance.AnalyzeAsync(url, ct);

            await Json(resp, result);
        }

        private async Task HandleYouTubeDownload(HttpListenerRequest req, HttpListenerResponse resp)
        {
            if (req.HttpMethod != "POST") { resp.StatusCode = 405; resp.Close(); return; }

            string body = await ReadBodyAsync(req);

            var payload = JsonSerializer.Deserialize<YoutubePayload>(body);

            if (payload == null || string.IsNullOrWhiteSpace(payload.Url))
            {
                await Json(resp, new { success = false, error = "url is required" }, 400);
                return;
            }

            var id = Guid.NewGuid().ToString();

            CFSIncomingHandler.RegisterYoutubePending(id, payload.FormatId ?? "bestvideo+bestaudio/best");

            var data = new FileTask
            {
                id = id,
                url = payload.Url,
                formatId = payload.FormatId ?? "bestvideo+bestaudio/best",
                saveTo = Helpers.GetDefaultFolder(),
                fileName = payload.Filename ?? string.Empty,
                title = payload.Title    ?? string.Empty,
                filesize = payload.Filesize,
            };
            Utils.AppRuntime.EnsureRunnerStarted(id, data);

            await Json(resp, new { success = true });
        }

        private static async Task<string> ReadBodyAsync(HttpListenerRequest req)
        {
            try
            {
                long len = req.ContentLength64; // -1 if not provided

                if (len == 0) return string.Empty;

                if (len > 0)
                {
                    byte[] buf = new byte[(int)len];
                    int offset = 0;
                    while (offset < len)
                    {
                        int read = await req.InputStream.ReadAsync(buf, offset, (int)(len - offset));
                        if (read == 0) break;
                        offset += read;
                    }
                    return Encoding.UTF8.GetString(buf, 0, offset);
                }

                // Content-Length missing — read with a 5-second timeout
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var ms = new System.IO.MemoryStream();
                byte[] tmp = new byte[4096];
                try
                {
                    int read;
                    while ((read = await req.InputStream.ReadAsync(tmp, cts.Token)) > 0)
                        ms.Write(tmp, 0, read);
                }
                catch (OperationCanceledException) { /* timeout — use what we have */ }

                return Encoding.UTF8.GetString(ms.ToArray());
            }
            catch
            {
                return string.Empty;
            }
        }

        private static async Task Json(HttpListenerResponse resp, object obj, int status = 200)
        {
            byte[] data = JsonSerializer.SerializeToUtf8Bytes(obj);
            resp.ContentType     = "application/json; charset=utf-8";
            resp.ContentLength64 = data.Length;
            resp.StatusCode      = status;
            await resp.OutputStream.WriteAsync(data);
            resp.Close();
        }

        private class DownloadPayload
        {
            [JsonPropertyName("url")]
            public string Url { get; set; } = string.Empty;

            [JsonPropertyName("saveTo")]
            public string? SaveTo { get; set; }

            [JsonPropertyName("fileName")]
            public string? FileName { get; set; }

            [JsonPropertyName("headers")]
            public Dictionary<string, string>? Headers { get; set; }
        }

        private class YoutubePayload
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
        }
    }
}
