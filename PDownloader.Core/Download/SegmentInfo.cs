namespace PDownloader.Core.Download
{
    public class SegmentInfo
    {
        public int Index { get; init; }
        public long RangeStart { get; init; }
        public long RangeEnd { get; init; }
        public long BytesWritten { get; set; }
        public string TempFilePath { get; init; } = string.Empty;
        public bool IsCompleted { get; set; }
        public long Length => RangeEnd - RangeStart + 1;
    }
}
