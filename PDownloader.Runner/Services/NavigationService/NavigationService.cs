namespace PDownloader.Runner.Services
{
    /// <summary>
    /// Managed host of the application.
    /// </summary>
    public class NavigationService : INavigationService
    {
        private readonly IServiceProvider? _serviceProvider;

        private readonly PowerModeService _powerModeService;

        private IWindow? _mainWindow;

        public NavigationService(IServiceProvider serviceProvider, PowerModeService powerModeService)
        {
            _serviceProvider = serviceProvider;
            _powerModeService = powerModeService;
        }

        public void NavigateTo(Type pageType)
        {
            _mainWindow = (
                _serviceProvider?.GetService(typeof(IWindow)) as IWindow
            )!;

            if (!typeof(UIElement).IsAssignableFrom(pageType))
            {
                throw new ArgumentException($"{pageType.Name} must inherit UIElement.");
            }

            if (_serviceProvider == null)
            {
                throw new Exception("serviceProvider not available.");
            }

            var page = (UIElement)_serviceProvider.GetRequiredService(pageType);

            _mainWindow.FrameHost.Navigate(page);

            _ = _powerModeService.OptimizeAfterAsync(TimeSpan.FromSeconds(2));
        }
    }
}
