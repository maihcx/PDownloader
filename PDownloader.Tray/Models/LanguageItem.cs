namespace PDownloader.Tray.Models
{
    public class LanguageItem
    {
        public string Code { get; set; } = string.Empty;
        public string NativeName { get; set; } = string.Empty;
        public string EnglishName { get; set; } = string.Empty;

        public override string ToString() => NativeName;

        public override bool Equals(object? obj)
        {
            return obj is LanguageItem other && this.Code == other.Code;
        }

        public override int GetHashCode()
        {
            return Code.GetHashCode();
        }
    }
}
