namespace TreeSizeTracker.Models;

public class ScanProgress
{
    public int DirectoriesScanned { get; set; }
    public string CurrentDirectory { get; set; } = string.Empty;
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public TimeSpan ElapsedTime => DateTime.UtcNow - StartTime;
    public bool IsScanning { get; set; }
    public string? CurrentPartition { get; set; }
}