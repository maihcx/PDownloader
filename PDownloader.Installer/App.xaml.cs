using PDownloader.Installer.Views;
using System.Windows;

namespace PDownloader.Installer
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            bool isUninstall = e.Args.Contains("--uninstall");
            var window = new MainWindow(isUninstall);
            window.Show();
        }
    }
}
