namespace PDownloader.Core.Models
{
    public class FileTask
    {
        public string id { get; set; } = string.Empty;

        public string url { get; set; } = string.Empty;

        public string formatId { get; set; } = string.Empty;

        public string saveTo { get; set; } = string.Empty;

        public string fileName { get; set; } = string.Empty;

        public string title { get; set; } = string.Empty;

        public long filesize { get; set; }

        public string downloadRunner { get; set; } = string.Empty;

        public Dictionary<string, string>? headers { get; set; }
    }
}
