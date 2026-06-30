namespace PDownloader.Runner.Services;

/// <summary>
/// Receives CFS messages from Core and raises events for the ViewModel.
/// Registered in Bootstrap.OnBeforeStartup via cfsMain.OnMessageReceived.
/// </summary>
public static class DownloadsChannel
{
    public static event Action<DownloadItemDto>? OnProgress;

    public static void Handle(string name, string value)
    {
        switch (name)
        {
            case "muxt-download-progress":
                HandleProgress(value);
                break;
        }
    }

    private static void HandleProgress(string value)
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
