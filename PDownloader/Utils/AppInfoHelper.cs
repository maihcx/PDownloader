namespace PDownloader.Utils
{
    public static class AppInfoHelper
    {
        public static readonly string AppName = Assembly.GetExecutingAssembly().GetName().Name ?? string.Empty;
        public static string Author = "Song Mai Software";
        public static string SortAuthor = "SM SOFT";
        public static string AuthorCreated = "Created by SM SOFT";
        public static string AppDescription = "A fast, lightweight download manager for Windows.";
        public static string CopyRight = "© 2026 Song Mai Software";

        public static string GetAppPath()
        {
            string? appPath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(appPath))
            {
                appPath = AppDomain.CurrentDomain.BaseDirectory;
            }
            else
            {
                appPath = Path.GetDirectoryName(appPath) ?? appPath;
            }
            return appPath.Replace("\\", "/");
        }

        public static string GetAppPackage()
        {
            string? exePath = Environment.ProcessPath;

            if (string.IsNullOrEmpty(exePath))
            {
                exePath = Assembly.GetEntryAssembly()?.Location;
            }

            if (string.IsNullOrEmpty(exePath))
            {
                return string.Empty;
            }

            return Path.GetFileName(exePath);
        }
    }
}
