using PDownloader.Models;

namespace PDownloader.Services
{
    public static class SharedMem
    {
        public static AppSettings? AppSettings { get; set; }

        public static DownloadLauncherService? Launcher { get; set; }

        private static bool _isScrollToUpdateCard = false;
        public static bool IsScrollToUpdateCard
        {
            get
            {
                bool v = _isScrollToUpdateCard;
                _isScrollToUpdateCard = false;
                return v;
            }
            set => _isScrollToUpdateCard = value;
        }
    }
}
