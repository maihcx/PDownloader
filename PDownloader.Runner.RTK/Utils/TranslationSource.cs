using System.ComponentModel;
using System.Globalization;
using System.Resources;
using PDownloader.Runner.Resources.Locales;

namespace PDownloader.Runner.Utils;

public class TranslationSource : INotifyPropertyChanged
{
    private static readonly TranslationSource _instance = new();
    public static TranslationSource Instance => _instance;

    private readonly ResourceManager _resourceManager = new(typeof(System.String));
    private CultureInfo _currentCulture = CultureInfo.InstalledUICulture;

    public string this[string key]
        => _resourceManager.GetString(key, _currentCulture) ?? key;

    public CultureInfo CurrentCulture
    {
        get => _currentCulture;
        set
        {
            if (_currentCulture != value)
            {
                _currentCulture = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
