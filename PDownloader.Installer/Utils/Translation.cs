using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace PDownloader.Installer.Utils
{
    /// <summary>
    /// Binding-friendly translation source (INotifyPropertyChanged).
    /// Mirrors the pattern used in PDownloader.Tray.
    /// </summary>
    public class TranslationSource : INotifyPropertyChanged
    {
        private static readonly TranslationSource _instance = new();
        public static TranslationSource Instance => _instance;

        private readonly ResourceManager _resManager =
            Resources.Locales.String.ResourceManager;

        private CultureInfo _currentCulture = CultureInfo.CurrentUICulture;

        public string this[string key] =>
            _resManager.GetString(key, _currentCulture) ?? key;

        public CultureInfo CurrentCulture
        {
            get => _currentCulture;
            set
            {
                if (!Equals(_currentCulture, value))
                {
                    _currentCulture = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>
    /// Markup extension – same approach as PDownloader.Tray LocalizationExtension.
    /// </summary>
    public class LocalizationExtension : System.Windows.Data.Binding
    {
        public LocalizationExtension(string key)
            : base($"[{key}]")
        {
            Mode = System.Windows.Data.BindingMode.OneWay;
            Source = TranslationSource.Instance;
        }
    }

    public static class LocalizationHelper
    {
        public static string Get(string key) =>
            TranslationSource.Instance[key];
    }

    public class LanguageItem
    {
        public string Code { get; set; } = "";
        public string NativeName { get; set; } = "";
        public string EnglishName { get; set; } = "";
        public override string ToString() => NativeName;
    }

    public static class LanguageBase
    {
        public static readonly List<CultureInfo> SupportedLanguages = new()
        {
            new CultureInfo("en"),
            new CultureInfo("vi"),
        };

        public static ObservableCollection<LanguageItem> GetLanguageItems()
        {
            var items = new ObservableCollection<LanguageItem>();
            foreach (var ci in SupportedLanguages)
                items.Add(new LanguageItem
                {
                    Code = ci.TwoLetterISOLanguageName,
                    NativeName = ci.NativeName,
                    EnglishName = ci.EnglishName,
                });
            return items;
        }

        public static void SetLanguage(string code)
        {
            TranslationSource.Instance.CurrentCulture = new CultureInfo(code);
        }
    }
}
