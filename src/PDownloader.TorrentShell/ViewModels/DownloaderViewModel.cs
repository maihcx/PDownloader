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

    [ObservableProperty]
    private TorrentShellConfig _torrentConfig;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartDownloadCommand))]
    private bool _isSubmitting;

    public DownloaderViewModel(
        INavigationService navigationService,
        TorrentShellConfig torrentConfig,
        TorrentShellService torrentShellService)
    {
        _navigationService = navigationService;
        _torrentConfig = torrentConfig;
        _torrentShellService = torrentShellService;

        foreach (TorrentFileViewModel file in TorrentConfig.Files)
        {
            file.PropertyChanged += File_PropertyChanged;
        }

        TranslationSource.Instance.PropertyChanged += TranslationSource_PropertyChanged;
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public int SelectedCount => TorrentConfig.Files.Count(file => file.IsSelected);
    public long SelectedBytes => TorrentConfig.Files
        .Where(file => file.IsSelected)
        .Sum(file => file.Length);
    public string SelectionSummary => LanguageBase.GetLangValue(
        "torrent_shell_selection_summary",
        SelectedCount,
        TorrentConfig.Files.Count,
        TorrentFileViewModel.FormatBytes(SelectedBytes));
    public bool CanStart => SelectedCount > 0 && !IsSubmitting;

    [RelayCommand]
    private void SelectAll() => SetAll(true);

    [RelayCommand]
    private void SelectNone() => SetAll(false);

    [RelayCommand]
    private void Cancel() => Application.Current.Shutdown();

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
                .StartDownloadsAsync(selected);
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

        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedBytes));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(CanStart));
        StartDownloadCommand.NotifyCanExecuteChanged();
    }

    private void TranslationSource_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(SelectionSummary));
    }
}
