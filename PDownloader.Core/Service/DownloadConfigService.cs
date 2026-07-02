namespace PDownloader.Core.Services.DownloadServices
{
    public class DownloadConfigService
    {
        private const string StoreKey = "pd-app-settings-v1";

        public DownloadConfigs? DownloadConfigs { get; private set; } = new DownloadConfigs();

        public DownloadConfigService()
        {
             LoadSettings(DownloadConfigs);
        }

        private void LoadSettings(DownloadConfigs? configs)
        {
            try
            {
                string? raw = UserDataStore.GetValue<string>(StoreKey);
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return;
                }

                var loaded = JsonSerializer.Deserialize<DownloadConfigs>(raw);
                if (loaded != null)
                {
                    CopyProperties(loaded, configs!);
                }
            }
            catch
            {
            }
        }

        private void CopyProperties(DownloadConfigs source, DownloadConfigs target)
        {
            foreach (var property in typeof(DownloadConfigs).GetProperties())
            {
                if (!property.CanRead || !property.CanWrite)
                    continue;

                property.SetValue(target, property.GetValue(source));
            }
        }

        public void Reload()
        {
            LoadSettings(DownloadConfigs);
        }
    }
}
