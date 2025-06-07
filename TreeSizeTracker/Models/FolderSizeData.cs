namespace TreeSizeTracker.Models;

public class FolderSizeData
{
    public int Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public long SizeInBytes { get; set; }
    public DateTime ScanDateTime { get; set; }
    public int FileCount { get; set; }
    public int SubfolderCount { get; set; }
}

public class FolderSizeDiff
{
    public string Path { get; set; } = string.Empty;
    public long PreviousSize { get; set; }
    public long CurrentSize { get; set; }
    public long SizeDifference => CurrentSize - PreviousSize;
    public double PercentageChange => PreviousSize == 0 ? 100 : ((double)SizeDifference / PreviousSize) * 100;
    public DateTime PreviousScanDate { get; set; }
    public DateTime CurrentScanDate { get; set; }
}