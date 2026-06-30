using System.Windows;
using PDownloader.Runner.ViewModels;

namespace PDownloader.Runner.Views;

public partial class RunnerWindow : Window
{
    public RunnerViewModel ViewModel { get; } = new RunnerViewModel();
    public RunnerWindow()
    {
        InitializeComponent(); 

        DataContext = this;
    }

    /// <summary>
    /// Called from RunnerCommandHandler when Core sends a new download request.
    /// Shows the window pre-filled; the user confirms or cancels.
    /// </summary>
    public void ShowForDownload(string url, string saveTo, string fileName)
    {
        Dispatcher.Invoke(() =>
        {
            ViewModel.LoadRequest(url, saveTo, fileName);
            if (!IsVisible) Show();
            Activate();
            Focus();
        });
    }

    private void OnTitleBarDrag(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) return;
        DragMove();
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        // Just hide — download continues in Core if already started
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Intercept close → hide instead
        if (AppRuntime.IsDownloadStated)
        {
            AppRuntime.Cfs?.Send("runner-ui-closed", "0");
        }
        else
        {
            AppRuntime.Cfs?.Send("runner-cancel-exp", "0");
        }
    }
}
