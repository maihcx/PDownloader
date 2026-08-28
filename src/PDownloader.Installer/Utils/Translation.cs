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

using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace PDownloader.Installer.Utils;

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
        foreach (CultureInfo ci in SupportedLanguages)
        {
            items.Add(new LanguageItem
            {
                Code = ci.TwoLetterISOLanguageName,
                NativeName = ci.NativeName,
                EnglishName = ci.EnglishName,
            });
        }

        return items;
    }

    public static void SetLanguage(string code)
    {
        TranslationSource.Instance.CurrentCulture = new CultureInfo(code);
    }
}
