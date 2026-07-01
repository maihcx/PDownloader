namespace PDownloader.Core.Utils
{
    internal static class JsonElementExt
    {
        public static string? GetStringOrDefault(this JsonElement el, string prop) =>
            el.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString() : null;
    }
}
