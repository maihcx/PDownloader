using Microsoft.Win32;
using System.IO;

namespace PDownloader.Installer.Services
{
    public static class BrowserExtensionInstallerService
    {
        public const string ExtensionId = "nliblbkhgljcpdboininiepogjaegien";

        private const string ForcelistSubKey = "ExtensionInstallForcelist";

        private static readonly (string DisplayName, string PolicyRoot)[] SupportedBrowsers =
        {
            ("Google Chrome",   @"SOFTWARE\Policies\Google\Chrome"),
            ("Microsoft Edge",  @"SOFTWARE\Policies\Microsoft\Edge"),
            ("Brave",           @"SOFTWARE\Policies\BraveSoftware\Brave"),
            ("Cốc Cốc",         @"SOFTWARE\Policies\CocCoc\CocCoc"),
        };

        public static void InstallForAllBrowsers(string installDir)
        {
            if (string.IsNullOrWhiteSpace(ExtensionId) ||
                ExtensionId.StartsWith("REPLACE_", StringComparison.Ordinal))
            {
                return;
            }

            string extensionDir = Path.Combine(installDir, "BrowserExtension");
            string updateManifestPath = Path.Combine(extensionDir, "update.xml");
            string crxPath = Path.Combine(extensionDir, "PDownloader.crx");

            if (!File.Exists(updateManifestPath) || !File.Exists(crxPath))
            {
                return;
            }

            string updateManifestUri = new Uri(updateManifestPath).AbsoluteUri;
            string forcelistEntry = $"{ExtensionId};{updateManifestUri}";

            foreach (var (_, policyRoot) in SupportedBrowsers)
            {
                try
                {
                    RegisterForceInstall(policyRoot, forcelistEntry);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.ToString());
                }
            }
        }

        public static void UninstallForAllBrowsers()
        {
            if (string.IsNullOrWhiteSpace(ExtensionId) ||
                ExtensionId.StartsWith("REPLACE_", StringComparison.Ordinal))
                return;

            foreach (var (_, policyRoot) in SupportedBrowsers)
            {
                try
                {
                    RemoveForceInstall(policyRoot);
                }
                catch { }
            }
        }

        private static void RegisterForceInstall(string policyRoot, string forcelistEntry)
        {
            using RegistryKey? baseKey = Registry.LocalMachine.CreateSubKey(policyRoot, writable: true);
            if (baseKey == null)
            {
                return;
            }

            using RegistryKey? forceList = baseKey.CreateSubKey(ForcelistSubKey, writable: true);
            if (forceList == null)
            {
                return;
            }

            string extensionId = forcelistEntry.Split(';')[0];

            foreach (string valueName in forceList.GetValueNames())
            {
                if (forceList.GetValue(valueName) is string existing &&
                    existing.StartsWith(extensionId, StringComparison.OrdinalIgnoreCase))
                {
                    forceList.SetValue(valueName, forcelistEntry);
                    return;
                }
            }

            int nextIndex = 1;
            var usedIndexes = forceList.GetValueNames()
                .Select(n => int.TryParse(n, out int i) ? i : 0)
                .ToHashSet();
            while (usedIndexes.Contains(nextIndex)) nextIndex++;

            forceList.SetValue(nextIndex.ToString(), forcelistEntry);
        }

        private static void RemoveForceInstall(string policyRoot)
        {
            using RegistryKey? baseKey = Registry.LocalMachine.OpenSubKey(policyRoot, writable: true);
            using RegistryKey? forceList = baseKey?.OpenSubKey(ForcelistSubKey, writable: true);
            if (forceList == null) return;

            foreach (string valueName in forceList.GetValueNames().ToArray())
            {
                if (forceList.GetValue(valueName) is string existing &&
                    existing.StartsWith(ExtensionId, StringComparison.OrdinalIgnoreCase))
                {
                    forceList.DeleteValue(valueName, throwOnMissingValue: false);
                }
            }
        }
    }
}
