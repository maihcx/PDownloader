namespace PDownloader.Models
{
    public partial class DownloadConfigs : ObservableObject
    {
        [ObservableProperty]
        public string _defaultDownloadFolder = string.Empty;

        [ObservableProperty]
        public int _defaultThreadCount = 8;
    }
}
