using System.Windows;
using PDownloader.Runner.Utils;

namespace PDownloader.Runner;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var bootstrap = new Bootstrap(e.Args, this);
        bootstrap.OnStarted();
    }
}
