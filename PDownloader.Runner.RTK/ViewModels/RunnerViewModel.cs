using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WpfApp = System.Windows.Application;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PDownloader.Runner.Utils;

namespace PDownloader.Runner.ViewModels;

public partial class RunnerViewModel : ObservableObject
{
    [ObservableProperty]
    private string _url       = string.Empty;

    [ObservableProperty]
    private string _fileName  = string.Empty;

    [ObservableProperty]
    private string _saveTo    = string.Empty;

    [ObservableProperty]
    private int    _threads   = 8;

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private bool   _hasError  = false;

    [ObservableProperty]
    private bool   _isSending = false;

    #region Download state
    public enum RunnerState { Form, Downloading, Completed }

    [ObservableProperty]
    private RunnerState _state = RunnerState.Form;

    [ObservableProperty]
    private double _progressPercent = 0;

    [ObservableProperty]
    private string _progressText = "0%";

    [ObservableProperty]
    private string _speedText = "";

    [ObservableProperty]
    private string _etaText = "";

    [ObservableProperty]
    private string _downloadedText = "";

    [ObservableProperty]
    private string _totalText = "";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _isPaused = false;

    [ObservableProperty]
    private double _progressRatio = 0;

    [ObservableProperty]
    private string _completedFilePath = string.Empty;

    public bool IsDownloading => State == RunnerState.Downloading;
    public bool IsForm => State == RunnerState.Form;

    partial void OnStateChanged(RunnerState value)
    {
        OnPropertyChanged(nameof(IsDownloading));
        OnPropertyChanged(nameof(IsForm));
    }

    partial void OnProgressPercentChanged(double value)
    {
        ProgressRatio = ProgressPercent / 100.0;
    }
    #endregion


    public RunnerViewModel()
    {
        _saveTo  = GetDefaultFolder();
        _threads = UserDataStore.GetValue<int>("DefaultThreads") is int t && t > 0 ? t : 8;

        DownloadsChannel.OnProgress += DownloadsChannel_OnProgress;
    }

    private void DownloadsChannel_OnProgress(DownloadItemDto obj)
    {
        System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            ProgressPercent  = obj.Progress;
            ProgressText     = $"{ProgressPercent:F0}%";
            SpeedText        = obj.SpeedFormatted;
            EtaText          = obj.EtaFormatted;
            DownloadedText   = obj.DownloadedFormatted;
            TotalText        = obj.TotalFormatted;
            StatusText       = obj.StatusText;

            if (obj.Status == "Completed")
            {
                CompletedFilePath = obj.SavePath;
                State = RunnerState.Completed;
            }

        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    public void LoadRequest(string url, string saveTo, string fileName)
    {
        Url       = url;
        FileName  = string.IsNullOrWhiteSpace(fileName) ? GuessFileName(url) : fileName;
        SaveTo    = string.IsNullOrWhiteSpace(saveTo)   ? GetDefaultFolder() : saveTo;
        HasError  = false;
        ErrorText = string.Empty;
        IsSending = false;
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description         = "Chọn thư mục lưu file",
            SelectedPath        = SaveTo,
            ShowNewFolderButton = true
        };
        if (dlg.ShowDialog() == DialogResult.OK)
            SaveTo = dlg.SelectedPath;
    }

    [RelayCommand]
    private async Task ConfirmDownload()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            ErrorText = "URL không hợp lệ.";
            HasError  = true;
            return;
        }
        if (string.IsNullOrWhiteSpace(SaveTo) || !Directory.Exists(SaveTo))
        {
            ErrorText = "Thư mục lưu không tồn tại.";
            HasError  = true;
            return;
        }

        HasError  = false;
        ErrorText = string.Empty;
        IsSending = true;

        // Save defaults
        UserDataStore.SetValue("DefaultDownloadFolder", SaveTo);
        UserDataStore.SetValue("DefaultThreads", Threads);

        var payload = JsonSerializer.Serialize(new
        {
            id       = AppRuntime.Config.Token,
            url      = Url,
            saveTo   = SaveTo,
            fileName = FileName,
            threads  = Threads
        });

        // Send on a background thread — pipe Connect() blocks the UI thread
        // and causes a deadlock when Core's SendBitOK() races with our responseClient.Connect()
        AppRuntime.IsDownloadStated = true;
        bool ok = await Task.Run(() => SendWithRetry(payload, retries: 3));

        IsSending = false;

        if (ok)
        {
            StatusText = "Đang kết nối…";
            State      = RunnerState.Downloading;
        }
        else
        {
            ErrorText = "Không thể kết nối tới PDownloader Core. Hãy chắc chắn ứng dụng đang chạy.";
            HasError  = true;
        }
    }

    [RelayCommand]
    private void PauseResume()
    {
        if (IsPaused)
        {
            AppRuntime.Cfs?.Send("runner-resume", AppRuntime.Config.Token);
            IsPaused = false;
        }
        else
        {
            AppRuntime.Cfs?.Send("runner-pause", AppRuntime.Config.Token);
            IsPaused = true;
        }
    }

    [RelayCommand]
    private void CancelDownload()
    {
        AppRuntime.Cfs?.Send("runner-cancel", AppRuntime.Config.Token);
        State    = RunnerState.Form;
        IsPaused = false;
        AppRuntime.MainWindow?.Close();
    }

    [RelayCommand]
    private void OpenFile()
    {
        if (!File.Exists(CompletedFilePath)) return;
        Process.Start(new ProcessStartInfo(CompletedFilePath) { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var folder = Path.GetDirectoryName(CompletedFilePath);
        if (folder is null || !Directory.Exists(folder)) return;
        // Mở Explorer và highlight file
        Process.Start("explorer.exe", $"/select,\"{CompletedFilePath}\"");
    }

    /// <summary>
    /// Retry up to <paramref name="retries"/> times with 500ms delay.
    /// Runs on a background thread — never call from UI thread.
    /// </summary>
    private static bool SendWithRetry(string payload, int retries)
    {
        for (int i = 0; i < retries; i++)
        {
            try
            {
                bool ok = AppRuntime.Cfs?.Send("runner-start-download", payload) ?? false;
                if (ok) return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Runner] Send attempt {i + 1} failed: {ex.Message}");
            }

            if (i < retries - 1)
                Thread.Sleep(500);
        }
        return false;
    }

    private static string GetDefaultFolder()
    {
        string? saved = UserDataStore.GetValue<string>("DefaultDownloadFolder");
        if (!string.IsNullOrWhiteSpace(saved) && Directory.Exists(saved)) return saved;
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\Downloads";
    }

    private static string GuessFileName(string url)
    {
        try
        {
            var p = new Uri(url).AbsolutePath;
            var s = p.Substring(p.LastIndexOf('/') + 1);
            return Uri.UnescapeDataString(s.Contains('.') ? s : "download");
        }
        catch { return "download"; }
    }
}