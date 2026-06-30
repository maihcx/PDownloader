namespace PDownloader.Runner.Services
{
    public interface INavigationAware
    {
        Task OnNavigatedToAsync();

        Task OnNavigatedFromAsync();
    }
}
