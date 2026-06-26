namespace PDownloader.Utils
{
    public static class StartupManager
    {
        private static string RegistryPath = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";

        public static void SetStartWithWin(bool value)
        {
            RegistryKey? registryKey = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
            if (value)
            {
                registryKey?.SetValue(AppInfoHelper.AppName, Process.GetCurrentProcess()?.MainModule?.FileName ?? string.Empty);
            }
            else
            {
                registryKey?.DeleteValue(AppInfoHelper.AppName, false);
            }
            registryKey?.Close();
            UserDataStore.SetValue("IsStartAtBoot", value);
        }

        public static void RefreshStartWithWin()
        {
            RegistryKey? registryKey = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
            if (UserDataStore.GetValue<bool>("IsStartAtBoot"))
            {
                registryKey?.SetValue(AppInfoHelper.AppName, Process.GetCurrentProcess()?.MainModule?.FileName ?? string.Empty);
            }
            else
            {
                registryKey?.DeleteValue(AppInfoHelper.AppName, false);
            }
            registryKey?.Close();
        }
    }
}
