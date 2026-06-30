namespace PDownloader.Services.HostServices
{
    public class PowerModeHostService : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;

        private IWindow? mainWindow = null;

        private PowerModeService? powerModeService = null;

        public PowerModeHostService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await HandleActivationAsync();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            mainWindow?.Activated -= MainWindow_Activated;

            mainWindow?.Deactivated -= MainWindow_Deactivated;

            mainWindow?.StateChanged -= MainWindow_StateChanged;

            return Task.CompletedTask;
        }

        private Task HandleActivationAsync()
        {
            if (mainWindow == null)
            {
                mainWindow = _serviceProvider.GetRequiredService<IWindow>();
            }

            if (powerModeService == null)
            {
                powerModeService = _serviceProvider.GetRequiredService<PowerModeService>();
            }

            mainWindow?.Activated += MainWindow_Activated;

            mainWindow?.Deactivated += MainWindow_Deactivated;

            mainWindow?.StateChanged += MainWindow_StateChanged;

            return Task.CompletedTask;
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (mainWindow?.WindowState == WindowState.Minimized)
            {
                powerModeService?.SetPowerMode(PowerModeService.PowerModeState.EfficiencyAdvanced);
            }
            else
            {
                powerModeService?.SetPowerMode(PowerModeService.PowerModeState.Normal);
                _ = powerModeService?.OptimizeAfterAsync(TimeSpan.FromSeconds(2));
            }
        }

        private void MainWindow_Deactivated(object? sender, EventArgs e)
        {
            if (mainWindow?.WindowState != WindowState.Minimized)
            {
                powerModeService?.SetPowerMode(PowerModeService.PowerModeState.Efficiency);
            }
        }

        private void MainWindow_Activated(object? sender, EventArgs e)
        {
            if (mainWindow?.WindowState != WindowState.Minimized)
            {
                powerModeService?.SetPowerMode(PowerModeService.PowerModeState.Normal);
            }
        }
    }
}
