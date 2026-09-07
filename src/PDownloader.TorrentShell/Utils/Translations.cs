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

namespace PDownloader.TorrentShell.Utils;

public sealed class LocalizationExtension : Binding
{
    public LocalizationExtension(string key)
        : base($"[{key}]")
    {
        Mode = BindingMode.OneWay;
        Source = TranslationSource.Instance;
    }
}

public sealed class TranslationSource : INotifyPropertyChanged
{
    private static readonly TranslationSource InstanceValue = new();
    private readonly ResourceManager _resourceManager =
        Resources.Locales.String.ResourceManager;
    private CultureInfo _currentCulture = CultureInfo.CurrentUICulture;

    private TranslationSource()
    {
    }

    public static TranslationSource Instance => InstanceValue;

    public string this[string key] =>
        _resourceManager.GetString(key, _currentCulture) ?? key;

    public CultureInfo CurrentCulture
    {
        get => _currentCulture;
        set
        {
            if (Equals(_currentCulture, value))
            {
                return;
            }

            _currentCulture = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public static class LanguageBase
{
    public static CultureInfo GetSetupLanguage()
    {
        string language = UserDataStore.GetValue("Language", "en");
        return new CultureInfo(string.IsNullOrWhiteSpace(language) ? "en" : language);
    }

    public static string GetLangValue(string key, params object[] args)
    {
        string raw = Resources.Locales.String.ResourceManager.GetString(
            key,
            TranslationSource.Instance.CurrentCulture) ?? key;

        return args.Length == 0
            ? raw
            : string.Format(TranslationSource.Instance.CurrentCulture, raw, args);
    }
}
