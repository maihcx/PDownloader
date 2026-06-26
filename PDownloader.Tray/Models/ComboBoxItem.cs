namespace PDownloader.Tray.Models
{
    public class ComboBoxItem
    {
        public string Value { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;

        public override string ToString() => Content;

        public override bool Equals(object? obj)
        {
            return obj is ComboBoxItem other && this.Value == other.Value;
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }
    }
}
