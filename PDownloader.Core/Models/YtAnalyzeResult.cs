namespace PDownloader.Core.Models
{
    public sealed class YtAnalyzeResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("formats")]
        public List<YtFormat>? Formats { get; init; }

        [JsonPropertyName("error")]
        public string? Error { get; init; }

        public static YtAnalyzeResult Fail(string error) =>
            new() { Success = false, Error = error };
    }
}
