using System.Text.Json.Serialization;

namespace PDownloader.Runner.Models;

public class DownloadRequest
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("saveTo")]
    public string? SaveTo { get; set; }

    [JsonPropertyName("fileName")]
    public string? FileName { get; set; }
}
