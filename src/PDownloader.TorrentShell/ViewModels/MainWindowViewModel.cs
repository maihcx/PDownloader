// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
//
// Copyright (C) Song Mai Software.

namespace PDownloader.TorrentShell.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly TorrentShellService _service;
    private readonly string _destinationSubfolder;
    private bool _isApplyingSession;
    private bool _isProgressVisible;
    private bool _isSubmitting;
    private string _saveTo = string.Empty;
    private TorrentCategoryItem? _selectedCategory;
    private bool _rememberPathForCategory;
    private string _errorMessage = string.Empty;
    private double _progress;
    private string _speedText = "–";
    private string _etaText = "–";
    private string _downloadedText = string.Empty;
    private string _totalText = string.Empty;
    private DownloadStatus _status = DownloadStatus.Queued;

    public MainWindowViewModel(TorrentShellService service)
    {
        _service = service;
        SelectAllCommand = new RelayCommand(() => SetAll(true));
        SelectNoneCommand = new RelayCommand(() => SetAll(false));
        StartCommand = new AsyncRelayCommand(StartAsync, () => CanStart);
        BrowseFolderCommand = new RelayCommand(BrowseFolder);
        PauseCommand = new RelayCommand(service.Pause);
        ResumeCommand = new RelayCommand(service.Resume);
        RetryCommand = new RelayCommand(service.Retry);
        CancelCommand = new RelayCommand(Cancel);
        OpenFolderCommand = new RelayCommand(OpenFolder);
        TorrentShellSessionView session = service.Session;
        TorrentName = string.IsNullOrWhiteSpace(session.Name)
            ? LanguageBase.GetLangValue("torrent_selector_default_name") : session.Name;
        InfoHash = session.InfoHash;
        _destinationSubfolder = session.DestinationSubfolder;
        _saveTo = session.SaveTo;
        foreach (DownloadCategoryDto category in session.Categories)
        {
            Categories.Add(new TorrentCategoryItem
            {
                Id = category.Id,
                Name = category.Name,
                FolderPath = category.FolderPath
            });
        }

        _isApplyingSession = true;
        SelectedCategory = Categories.FirstOrDefault(category =>
            string.Equals(category.Id, session.SelectedCategoryId, StringComparison.OrdinalIgnoreCase))
            ?? Categories.FirstOrDefault();
        _isApplyingSession = false;

        foreach (TorrentFileProgressDto file in session.Files)
        {
            var item = new TorrentFileItemViewModel(file)
            {
                IsSelected = session.IsStarted || file.Status != DownloadStatus.Cancelled
            };
            item.PropertyChanged += File_PropertyChanged;
            Files.Add(item);
        }

        IsProgressVisible = session.IsStarted;
        if (session.IsStarted)
        {
            TotalText = TorrentFileItemViewModel.FormatBytes(session.TotalBytes);
            DownloadedText = TorrentFileItemViewModel.FormatBytes(
                session.Files.Sum(file => file.DownloadedBytes));
        }

        service.ProgressChanged += Service_ProgressChanged;
        TranslationSource.Instance.PropertyChanged += TranslationSource_PropertyChanged;
    }

    public event Action? CloseRequested;
    public string TorrentName { get; }
    public string InfoHash { get; }
    public ObservableCollection<TorrentFileItemViewModel> Files { get; } = [];
    public ObservableCollection<TorrentCategoryItem> Categories { get; } = [];
    public IRelayCommand SelectAllCommand { get; }
    public IRelayCommand SelectNoneCommand { get; }
    public IAsyncRelayCommand StartCommand { get; }
    public IRelayCommand BrowseFolderCommand { get; }
    public IRelayCommand PauseCommand { get; }
    public IRelayCommand ResumeCommand { get; }
    public IRelayCommand RetryCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand OpenFolderCommand { get; }

    public bool IsProgressVisible
    {
        get => _isProgressVisible;
        private set
        {
            if (SetProperty(ref _isProgressVisible, value))
            {
                OnPropertyChanged(nameof(IsSelectionVisible));
            }
        }
    }
    public bool IsSelectionVisible => !IsProgressVisible;
    public bool IsSubmitting
    {
        get => _isSubmitting;
        private set { if (SetProperty(ref _isSubmitting, value))
            {
                RefreshCanStart();
            }
        }
    }
    public string SaveTo
    {
        get => _saveTo;
        set { if (SetProperty(ref _saveTo, value))
            {
                RefreshCanStart();
            }
        }
    }
    public TorrentCategoryItem? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (!SetProperty(ref _selectedCategory, value))
            {
                return;
            }

            if (!_isApplyingSession && value is not null)
            {
                SaveTo = CombineSubfolder(value.FolderPath);
            }

            OnPropertyChanged(nameof(RememberPathLabel));
        }
    }
    public bool RememberPathForCategory
    {
        get => _rememberPathForCategory;
        set => SetProperty(ref _rememberPathForCategory, value);
    }
    public string RememberPathLabel => LanguageBase.GetLangValue(
        "remember_group_path_title", SelectedCategory?.Name ?? string.Empty);
    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }
    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, NormalizeProgress(value));
    }
    public string ProgressText => $"{Progress:F0}%";
    public string SpeedText { get => _speedText; private set => SetProperty(ref _speedText, value); }
    public string EtaText { get => _etaText; private set => SetProperty(ref _etaText, value); }
    public string DownloadedText { get => _downloadedText; private set => SetProperty(ref _downloadedText, value); }
    public string TotalText { get => _totalText; private set => SetProperty(ref _totalText, value); }
    public DownloadStatus Status
    {
        get => _status;
        private set
        {
            if (!SetProperty(ref _status, value))
            {
                return;
            }

            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(IsPauseVisible));
            OnPropertyChanged(nameof(IsResumeVisible));
            OnPropertyChanged(nameof(IsRetryVisible));
            OnPropertyChanged(nameof(IsCompleted));
        }
    }
    public string StatusText => Status == DownloadStatus.Error
        ? LanguageBase.GetLangValue("download_status_error_title", ErrorMessage)
        : Status == DownloadStatus.Retrying
            ? LanguageBase.GetLangValue("download_status_retry_title", ErrorMessage)
            : LanguageBase.GetLangValue(Status switch
        {
            DownloadStatus.Queued => "download_status_queued_title",
            DownloadStatus.Connecting => "download_status_connecting_title",
            DownloadStatus.Downloading => "download_status_downloading_title",
            DownloadStatus.Paused => "download_status_paused_title",
            DownloadStatus.Completed => "download_status_completed_title",
            DownloadStatus.Retrying => "download_status_retry_title",
            _ => "download_status_queued_title"
        });
    public bool IsPauseVisible => Status == DownloadStatus.Downloading;
    public bool IsResumeVisible => Status == DownloadStatus.Paused;
    public bool IsRetryVisible => Status == DownloadStatus.Error;
    public bool IsCompleted => Status == DownloadStatus.Completed;
    public int SelectedCount => Files.Count(file => file.IsSelected);
    public long SelectedBytes => Files.Where(file => file.IsSelected).Sum(file => file.Length);
    public string SelectionSummary => LanguageBase.GetLangValue("torrent_selector_selection_summary",
        SelectedCount, Files.Count, TorrentFileItemViewModel.FormatBytes(SelectedBytes));
    public bool CanStart => SelectedCount > 0 && !IsSubmitting && !string.IsNullOrWhiteSpace(SaveTo);

    private void SetAll(bool selected)
    {
        foreach (TorrentFileItemViewModel file in Files)
        {
            file.IsSelected = selected;
        }
    }

    private void File_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TorrentFileItemViewModel.IsSelected))
        {
            return;
        }

        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedBytes));
        OnPropertyChanged(nameof(SelectionSummary));
        RefreshCanStart();
    }

    private async Task StartAsync()
    {
        IsSubmitting = true;
        ErrorMessage = string.Empty;
        try
        {
            bool sent = await _service.StartDownloadAsync(new TorrentShellStartRequest
            {
                SelectedFileIndexes = Files.Where(file => file.IsSelected).Select(file => file.Index).ToList(),
                SaveTo = SaveTo,
                CategoryId = SelectedCategory?.Id ?? string.Empty,
                RememberPathForCategory = RememberPathForCategory
            });
            if (!sent)
            {
                ErrorMessage = LanguageBase.GetLangValue("torrent_selector_send_selection_error");
                return;
            }

            foreach (TorrentFileItemViewModel unselected in Files.Where(file => !file.IsSelected).ToArray())
            {
                Files.Remove(unselected);
            }

            Status = DownloadStatus.Connecting;
            IsProgressVisible = true;
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsSubmitting = false; }
    }

    private void Service_ProgressChanged(DownloadItemDto dto)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            IsProgressVisible = true;
            Progress = dto.Progress;
            OnPropertyChanged(nameof(ProgressText));
            SpeedText = dto.SpeedFormatted;
            EtaText = dto.EtaFormatted;
            DownloadedText = dto.DownloadedFormatted;
            TotalText = dto.TotalFormatted;
            ErrorMessage = dto.ErrorMessage;
            Status = dto.Status;
            foreach (TorrentFileProgressDto update in dto.TorrentFiles)
            {
                Files.FirstOrDefault(file => file.Index == update.Index)?.Apply(update);
            }
        });
    }

    private void BrowseFolder()
    {
        string initial = Directory.Exists(SaveTo)
            ? SaveTo : Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(SaveTo)) ?? SaveTo;
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = LanguageBase.GetLangValue("select_folder_title"),
            InitialDirectory = initial
        };
        if (dialog.ShowDialog() == true)
        {
            SaveTo = CombineSubfolder(dialog.FolderName);
        }
    }

    private string CombineSubfolder(string root) => string.IsNullOrWhiteSpace(_destinationSubfolder)
        || string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(root)),
            _destinationSubfolder, StringComparison.OrdinalIgnoreCase)
        ? root : Path.Combine(root, _destinationSubfolder);

    private void Cancel()
    {
        if (IsProgressVisible)
        {
            _service.Cancel();
        }

        CloseRequested?.Invoke();
    }

    private void OpenFolder()
    {
        if (!Directory.Exists(SaveTo))
        {
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe", Arguments = $"\"{SaveTo}\"", UseShellExecute = true
        });
    }

    private void RefreshCanStart()
    {
        OnPropertyChanged(nameof(CanStart));
        StartCommand.NotifyCanExecuteChanged();
    }

    private void TranslationSource_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(RememberPathLabel));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(StatusText));
        foreach (TorrentFileItemViewModel file in Files)
        {
            file.RefreshLanguage();
        }
    }

    private static double NormalizeProgress(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;
}
