namespace PDownloader.Services.UpdateServices
{
    public enum UpdateStatus
    {
        Idle,
        Checking,
        UpdateAvailable,
        Downloading,
        ReadyToInstall,
        UpToDate,
        Error,
    }
}
