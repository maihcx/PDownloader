using PDownloader.Installer.ViewModels;
using System.Windows;
using Wpf.Ui.Controls;

namespace PDownloader.Installer.Views
{
    public partial class MainWindow : FluentWindow
    {
        public MainWindow(bool uninstallMode = false)
        {
            DataContext = new InstallerViewModel(uninstallMode);
            InitializeComponent();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Application.Current.Shutdown();
        }
    }
}
