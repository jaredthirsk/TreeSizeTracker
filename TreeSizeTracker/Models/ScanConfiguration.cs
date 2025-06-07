namespace TreeSizeTracker.Models;

public class GlobalConfiguration
{
    public Dictionary<string, ScanConfiguration> PartitionConfigurations { get; set; } = new();
    public string CronSchedule { get; set; } = "0 0 * * *"; // Daily at midnight
    public bool IsScheduledScanEnabled { get; set; } = true;
}

public class ScanConfiguration
{
    public string PartitionPath { get; set; } = string.Empty;
    public List<RootFolder> RootFolders { get; set; } = new();
    public List<ExclusionRule> ExclusionRules { get; set; } = new();
    public List<InclusionOverride> InclusionOverrides { get; set; } = new();
    public int DefaultScanDepth { get; set; } = int.MaxValue; // Scan all levels by default
    public bool IsEnabled { get; set; } = true; // Enable/disable scanning for this partition
}

public class RootFolder
{
    public string Path { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public int? MaxDepth { get; set; } // Override default depth for this root
}

public class InclusionOverride
{
    public string Path { get; set; } = string.Empty;
    public int ScanDepth { get; set; } = 1; // How deep to scan this specific folder
    public bool IsEnabled { get; set; } = true;
    public string Description { get; set; } = string.Empty;
    public bool ForceInclude { get; set; } = true; // If true, overrides exclusion rules
}

public class ExclusionRule
{
    public string Pattern { get; set; } = string.Empty;
    public ExclusionType Type { get; set; } = ExclusionType.Path;
    public bool IsEnabled { get; set; } = true;
    public string Description { get; set; } = string.Empty;
}

public enum ExclusionType
{
    Path,        // Exact path match
    PathPrefix,  // Path starts with
    FolderName,  // Folder name matches (any level)
    Wildcard,    // Simple wildcard pattern (*, ?)
    Regex        // Regular expression
}