namespace PDownloader.ControlsLookup
{
    [AttributeUsage(AttributeTargets.Class)]
    public class PageMetaAttribute : Attribute, INotifyPropertyChanged
    {
        public string DisplayName { get => Resources.Locales.String.ResourceManager.GetString(DisplayNameKey, TranslationSource.Instance.CurrentCulture) ?? string.Empty; }
        public string DisplayNameKey { get; }
        public string Description { get => Resources.Locales.String.ResourceManager.GetString(DescriptionKey, TranslationSource.Instance.CurrentCulture) ?? string.Empty; }
        public string DescriptionKey { get; }
        public SymbolRegular Icon { get; }
        public int SortIndex { get; }
        public bool IsShowPageTitle = true;

        public PageMetaAttribute(string displayName, string description, SymbolRegular icon, int sortIndex, bool isShowPageTitle)
        {
            DisplayNameKey = displayName;
            DescriptionKey = description;
            Icon = icon;
            SortIndex = sortIndex;
            IsShowPageTitle = isShowPageTitle;

            TranslationSource.Instance.PropertyChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(Description));
            };
        }

        public PageMetaAttribute(string displayName, string description, SymbolRegular icon, int sortIndex)
        {
            DisplayNameKey = displayName;
            DescriptionKey = description;
            Icon = icon;
            SortIndex = sortIndex;
        }

        public PageMetaAttribute(string displayName, string description, SymbolRegular icon)
        {
            DisplayNameKey = displayName;
            DescriptionKey = description;
            Icon = icon;
        }

        public PageMetaAttribute(string displayName, SymbolRegular icon)
        {
            DisplayNameKey = displayName;
            Icon = icon;
            DescriptionKey = string.Empty;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
