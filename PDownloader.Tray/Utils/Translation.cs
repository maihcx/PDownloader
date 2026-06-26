namespace PDownloader.Tray.Utils
{
    public class LocalizationExtension : Binding
    {
        public LocalizationExtension(string key)
            : base($"[{key}]")  // binding đến indexer của TranslationSource
        {
            Mode = BindingMode.OneWay;
            Source = TranslationSource.Instance;
        }
    }

    public class TranslationSource : INotifyPropertyChanged
    {
        private static readonly TranslationSource instance = new TranslationSource();
        public static TranslationSource Instance => instance;

        private readonly ResourceManager resManager = Resources.Locales.String.ResourceManager;
        private CultureInfo currentCulture = CultureInfo.CurrentUICulture;

        public string? this[string key] => resManager.GetString(key, currentCulture);

        public CultureInfo CurrentCulture
        {
            get => currentCulture;
            set
            {
                if (currentCulture != value)
                {
                    currentCulture = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
                }
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public static class LanguageBase
    {
        public static List<CultureInfo> SupportedLanguages { get; } = new List<CultureInfo>
        {
            new CultureInfo("en"),
            new CultureInfo("vi")
        };

        public delegate void LanguageChangedEventHandler(string language);
        public static event LanguageChangedEventHandler? LanguageChanged;

        public static ObservableCollection<LanguageItem> GetLanguageItems()
        {
            ObservableCollection<LanguageItem> languageItems = new ObservableCollection<LanguageItem>();

            foreach (var item in SupportedLanguages)
            {
                languageItems.Add(new LanguageItem()
                {
                    Code = item.TwoLetterISOLanguageName,
                    NativeName = item.NativeName,
                    EnglishName = item.EnglishName,
                });
            }

            return languageItems;
        }

        public static void SetLanguage(string language)
        {
            TranslationSource.Instance.CurrentCulture = new CultureInfo(language);
            UserDataStore.SetValue("Language", language);
            LanguageChanged?.Invoke(language);
        }

        public static CultureInfo GetSetupLanguage()
        {
            return new CultureInfo(UserDataStore.GetValue<string>("Language"));
        }

        public static CultureInfo GetCurrentLanguage()
        {
            return TranslationSource.Instance.CurrentCulture;
        }

        public static LanguageItem GetCurrentLanguageItem()
        {
            var currentLang = GetCurrentLanguage();
            return new LanguageItem()
            {
                Code = currentLang.TwoLetterISOLanguageName,
                NativeName = currentLang.NativeName,
                EnglishName = currentLang.EnglishName,
            };
        }
    }
}
