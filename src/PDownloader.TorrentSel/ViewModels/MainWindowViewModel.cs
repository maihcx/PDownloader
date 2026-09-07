// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace PDownloader.TorrentSel.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly TorrentSelectionService _service;
    private readonly string _torrentName;
    private bool _isSubmitting;
    private string _errorMessage = string.Empty;

    public MainWindowViewModel(TorrentSelectionService service)
    {
        _service = service;
        TorrentSelectionSessionView session = service.Session;
        _torrentName = session.Name;
        foreach (TorrentSelectionFileDto file in session.Files)
        {
            var item = new TorrentSelectionFileItem(file);
            item.PropertyChanged += File_PropertyChanged;
            Files.Add(item);
        }

        SelectAllCommand = new RelayCommand(() => SetAll(true));
        SelectNoneCommand = new RelayCommand(() => SetAll(false));
        ConfirmCommand = new AsyncRelayCommand(ConfirmAsync, () => CanConfirm);
        TranslationSource.Instance.PropertyChanged += TranslationSource_PropertyChanged;
    }

    public event Action? CloseRequested;
    public string TorrentName => string.IsNullOrWhiteSpace(_torrentName)
        ? LanguageBase.GetLangValue("torrent_selector_default_name")
        : _torrentName;
    public ObservableCollection<TorrentSelectionFileItem> Files { get; } = [];
    public IRelayCommand SelectAllCommand { get; }
    public IRelayCommand SelectNoneCommand { get; }
    public IAsyncRelayCommand ConfirmCommand { get; }

    public bool IsSubmitting
    {
        get => _isSubmitting;
        private set
        {
            if (SetProperty(ref _isSubmitting, value))
            {
                OnPropertyChanged(nameof(CanConfirm));
                ConfirmCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public int SelectedCount => Files.Count(file => file.IsSelected);
    public long SelectedBytes => Files.Where(file => file.IsSelected).Sum(file => file.Source.Length);
    public string SelectionSummary => LanguageBase.GetLangValue(
        "torrent_selector_selection_summary",
        SelectedCount,
        Files.Count,
        TorrentSelectionFileItem.FormatBytes(SelectedBytes));
    public bool CanConfirm => SelectedCount > 0 && !IsSubmitting;

    private void SetAll(bool selected)
    {
        foreach (TorrentSelectionFileItem file in Files)
        {
            file.IsSelected = selected;
        }
    }

    private void File_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TorrentSelectionFileItem.IsSelected))
        {
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(SelectedBytes));
            OnPropertyChanged(nameof(SelectionSummary));
            OnPropertyChanged(nameof(CanConfirm));
            ConfirmCommand.NotifyCanExecuteChanged();
        }
    }

    private void TranslationSource_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(TorrentName));
        OnPropertyChanged(nameof(SelectionSummary));
    }

    private async Task ConfirmAsync()
    {
        IsSubmitting = true;
        ErrorMessage = string.Empty;
        try
        {
            int[] selected = Files.Where(file => file.IsSelected)
                .Select(file => file.Index)
                .ToArray();
            if (!await _service.ConfirmAsync(selected))
            {
                ErrorMessage = LanguageBase.GetLangValue(
                    "torrent_selector_send_selection_error");
                return;
            }

            CloseRequested?.Invoke();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsSubmitting = false;
        }
    }
}
