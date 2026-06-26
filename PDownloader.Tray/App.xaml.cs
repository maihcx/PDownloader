using System.Windows.Interop;
using System.Windows.Media;

namespace PDownloader.Tray
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private string logFile;
        public App()
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
            logFile = Path.Combine(appPath, "crashTray.log");

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            RenderOptions.ProcessRenderMode = RenderMode.Default;
        }

        private void OnStartup(object sender, StartupEventArgs e)
        {
            TranslationSource.Instance.CurrentCulture = LanguageBase.GetSetupLanguage();

            MainWindow mainWindow = new MainWindow();
            mainWindow.Show();
        }

        public void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            File.AppendAllText(logFile, $"[{DateTime.Now}] UnhandledException: {ex}\n");
        }

        public void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            File.AppendAllText(logFile, $"[{DateTime.Now}] UnobservedTaskException: {e.Exception}\n");
            e.SetObserved();
        }
    }
}
