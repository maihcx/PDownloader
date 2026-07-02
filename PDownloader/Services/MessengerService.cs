namespace PDownloader.Services
{
    public static class MessengerService
    {
        private static readonly ISnackbarService GlobalSnackbar = App.GetRequiredService<ISnackbarService>();

        public static async void ShowSnackbar(string title, string content, ControlAppearance controlAppearance)
        {
            ShowSnackbar(title, content, controlAppearance, null, default);
        }

        public static async void ShowSnackbar(string title, string content, ControlAppearance controlAppearance, TimeSpan timeSpan = default)
        {
            ShowSnackbar(title, content, controlAppearance, null, timeSpan);
        }

        public static async void ShowSnackbar(string title, string content, ControlAppearance controlAppearance, IconElement? icon = null)
        {
            ShowSnackbar(title, content, controlAppearance, icon, default);
        }

        public static async void ShowSnackbar(string title, string content, ControlAppearance controlAppearance, IconElement? icon = null, TimeSpan timeSpan = default)
        {
            GlobalSnackbar.Show(LanguageBase.GetLangValue(title), LanguageBase.GetLangValue(content), controlAppearance, icon, timeSpan);
        }
    }
}
