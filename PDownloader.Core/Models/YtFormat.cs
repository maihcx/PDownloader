namespace PDownloader.Core.Models
{
    public sealed class YtFormat
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = "";

        [JsonPropertyName("ext")]
        public string Ext { get; init; } = "";

        [JsonPropertyName("height")]
        public int? Height { get; init; }

        [JsonPropertyName("note")]
        public string Note { get; init; } = "";

        [JsonPropertyName("size")]
        public string Size { get; init; } = "";

        [JsonPropertyName("filesize")]
        public long Filesize { get; init; }
    }
}
