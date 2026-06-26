using System.Windows.Controls;
using PDownloader.ViewModels.Pages;

namespace PDownloader.Views.Pages;

public partial class DownloadsPage : Page
{
    public DownloadsViewModel ViewModel { get; }

    public DownloadsPage(DownloadsViewModel vm)
    {
        ViewModel = vm;
        InitializeComponent();
        DataContext = this;
    }
}
