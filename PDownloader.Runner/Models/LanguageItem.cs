namespace PDownloader.Runner.Models
{
    public class LanguageItem
    {
        public string? Code { get; set; }
        public string? NativeName { get; set; }
        public string? EnglishName { get; set; }

        public override string ToString() => NativeName ?? string.Empty;

        public override bool Equals(object? obj)
        {
            return obj is LanguageItem other && this.Code == other.Code;
        }

        public override int GetHashCode()
        {
            return Code!.GetHashCode();
        }
    }
}
