namespace PDownloader.Runner.Models
{
    public partial class DownloaderServiceStatus : ObservableObject
    {
        public DownloaderServiceStatus()
        {
            LanguageBase.LanguageChanged += LanguageBase_LanguageChanged;
        }

        private void LanguageBase_LanguageChanged(string language)
        {
            OnErrorKeyChanged(ErrorKey);
            OnErrorKeyChanged(StatusKey);
        }

        [ObservableProperty]
        private string _errorKey = string.Empty;

        [ObservableProperty]
        private string _errorText = string.Empty;

        [ObservableProperty]
        private string _statusKey = string.Empty;

        [ObservableProperty]
        private string _statusText = string.Empty;

        [ObservableProperty]
        private bool _hasError = false;

        [ObservableProperty]
        private bool _isSending = false;

        [ObservableProperty]
        private bool _isPaused = false;

        [ObservableProperty]
        private RunnerState _state = RunnerState.Form;

        partial void OnErrorKeyChanged(string value)
        {
            ErrorText = LanguageBase.GetLangValue(value);
        }

        partial void OnStatusKeyChanged(string value)
        {
            StatusText = LanguageBase.GetLangValue(value);
        }
    }
}
