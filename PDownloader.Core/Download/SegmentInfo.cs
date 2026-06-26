namespace PDownloader.Core.Download;

/// <summary>One byte-range segment for a parallel/chunked download.</summary>
public class SegmentInfo
{
    public int    Index          { get; init; }
    public long   RangeStart     { get; init; }
    public long   RangeEnd       { get; init; }      // inclusive
    public long   BytesWritten   { get; set; }
    public string TempFilePath   { get; init; } = string.Empty;
    public bool   IsCompleted    => BytesWritten >= (RangeEnd - RangeStart + 1);
    public long   Length         => RangeEnd - RangeStart + 1;
}
