namespace PDownloader.Services.DownloadServices;

/// <summary>
/// Receives CFS messages from Core and raises events for the ViewModel.
/// Registered in Bootstrap.OnBeforeStartup via cfsMain.OnMessageReceived.
/// </summary>
public class DownloadsChannelService
{
    public event Action<List<DownloadItemDto>>? OnList;
    public event Action<DownloadItemDto>?       OnProgress;

    public void Handle(string name, string value)
    {
        switch (name)
        {
            case "muxt-get-downloader-list":
                HandleList(value);
                break;

            case "muxt-download-progress":
                HandleProgress(value);
                break;
        }
    }

    private void HandleList(string value)
    {
        try
        {
            var list = JsonSerializer.Deserialize<List<DownloadItemDto>>(value,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (list != null) OnList?.Invoke(list);
        }
        catch { }
    }

    private void HandleProgress(string value)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<DownloadItemDto>(value,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (dto != null) OnProgress?.Invoke(dto);
        }
        catch { }
    }
}
