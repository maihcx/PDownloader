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

public partial class DownloaderViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private readonly TorrentShellService _torrentShellService;
    private readonly HashSet<TorrentFileViewModel> _trackedFiles = [];

    [ObservableProperty]
    private TorrentShellConfig _torrentConfig;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartDownloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectNoneCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyPropertyChangedFor(nameof(CanInteract))]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    private bool _isSubmitting;

    public DownloaderViewModel(
        INavigationService navigationService,
        TorrentShellConfig torrentConfig,
        TorrentShellService torrentShellService)
    {
        _navigationService = navigationService;
        _torrentConfig = torrentConfig;
        _torrentShellService = torrentShellService;

        TorrentConfig.PropertyChanged += TorrentConfig_PropertyChanged;
        TorrentConfig.Files.CollectionChanged += Files_CollectionChanged;
        AttachFileHandlers();
        ErrorMessage = TorrentConfig.MetadataError;
        TranslationSource.Instance.PropertyChanged += TranslationSource_PropertyChanged;
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool IsLoading => TorrentConfig.IsLoading;
    public bool CanInteract => !TorrentConfig.IsLoading
        && !TorrentConfig.HasMetadataError
        && !IsSubmitting;
    public bool CanCancel => !TorrentConfig.IsLoading && !IsSubmitting;
    public int SelectedCount => TorrentConfig.Files.Count(file => file.IsSelected);
    public long SelectedBytes => TorrentConfig.Files
        .Where(file => file.IsSelected)
        .Sum(file => file.Length);
    public string SelectionSummary => LanguageBase.GetLangValue(
        "torrent_shell_selection_summary",
        SelectedCount,
        TorrentConfig.Files.Count,
        TorrentFileViewModel.FormatBytes(SelectedBytes));
    public bool CanStart => SelectedCount > 0 && CanInteract;

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private void SelectAll() => SetAll(true);

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private void SelectNone() => SetAll(false);

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => Application.Current.Shutdown();

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private void BrowseFolder()
    {
        string initialDirectory = TorrentConfig.SaveTo;
        if (!Directory.Exists(initialDirectory)
            && !string.IsNullOrWhiteSpace(TorrentConfig.DestinationSubfolder))
        {
            initialDirectory = Path.GetDirectoryName(
                Path.TrimEndingDirectorySeparator(initialDirectory))
                ?? initialDirectory;
        }

        var dialog = new OpenFolderDialog
        {
            Title = LanguageBase.GetLangValue("select_folder_title"),
            InitialDirectory = initialDirectory
        };
        if (dialog.ShowDialog() == true)
        {
            TorrentConfig.SaveTo = EnsureDestinationSubfolder(
                dialog.FolderName,
                TorrentConfig.DestinationSubfolder);
        }
    }

    private static string EnsureDestinationSubfolder(
        string saveTo,
        string destinationSubfolder)
    {
        if (string.IsNullOrWhiteSpace(destinationSubfolder)
            || string.Equals(
                Path.GetFileName(Path.TrimEndingDirectorySeparator(saveTo)),
                destinationSubfolder,
                StringComparison.OrdinalIgnoreCase))
        {
            return saveTo;
        }

        return Path.Combine(saveTo, destinationSubfolder);
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartDownload()
    {
        IsSubmitting = true;
        ErrorMessage = string.Empty;
        try
        {
            int[] selected = TorrentConfig.Files
                .Where(file => file.IsSelected)
                .Select(file => file.Index)
                .ToArray();
            TorrentShellStartResult result = await _torrentShellService
                .StartDownloadsAsync(
                    selected,
                    TorrentConfig.SaveTo,
                    TorrentConfig.SelectedCategory?.Id ?? string.Empty,
                    TorrentConfig.RememberPathForCategory);
            if (!result.Success)
            {
                ErrorMessage = string.IsNullOrWhiteSpace(result.Error)
                    ? LanguageBase.GetLangValue("torrent_shell_start_error")
                    : result.Error;
                return;
            }

            _navigationService.NavigateTo(typeof(DownloaderProgressPage));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    private void SetAll(bool selected)
    {
        foreach (TorrentFileViewModel file in TorrentConfig.Files)
        {
            file.IsSelected = selected;
        }
    }

    private void File_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TorrentFileViewModel.IsSelected))
        {
            return;
        }

        RefreshSelectionState();
    }

    private void Files_CollectionChanged(
        object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        AttachFileHandlers();
        RefreshSelectionState();
    }

    private void AttachFileHandlers()
    {
        foreach (TorrentFileViewModel file in _trackedFiles)
        {
            file.PropertyChanged -= File_PropertyChanged;
        }

        _trackedFiles.Clear();
        foreach (TorrentFileViewModel file in TorrentConfig.Files)
        {
            file.PropertyChanged += File_PropertyChanged;
            _trackedFiles.Add(file);
        }
    }

    private void TorrentConfig_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TorrentShellConfig.IsLoading)
            && e.PropertyName != nameof(TorrentShellConfig.MetadataError))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(TorrentConfig.MetadataError))
        {
            ErrorMessage = TorrentConfig.MetadataError;
        }
        else if (TorrentConfig.IsLoading)
        {
            ErrorMessage = string.Empty;
        }

        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(CanInteract));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanStart));
        NotifyCommandStates();
    }

    private void RefreshSelectionState()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedBytes));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(CanStart));
        NotifyCommandStates();
    }

    private void NotifyCommandStates()
    {
        SelectAllCommand.NotifyCanExecuteChanged();
        SelectNoneCommand.NotifyCanExecuteChanged();
        BrowseFolderCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        StartDownloadCommand.NotifyCanExecuteChanged();
    }

    private void TranslationSource_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(SelectionSummary));
    }
}
