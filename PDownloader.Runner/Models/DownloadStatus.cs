namespace PDownloader.Runner.Models
{
    public enum DownloadStatus
    {
        Queued,
        Connecting,
        Downloading,
        Paused,
        Merging,
        Completed,
        Cancelled,
        Error
    }
}
