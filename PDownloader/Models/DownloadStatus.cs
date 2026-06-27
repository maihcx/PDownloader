namespace PDownloader.Models
{
    public enum DownloadStatus
    {
        Queued,
        Connecting,
        Downloading,
        Paused,
        Merging,
        Completed,
        Error
    }
}
