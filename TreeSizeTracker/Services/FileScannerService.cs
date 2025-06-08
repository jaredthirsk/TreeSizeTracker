using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using TreeSizeTracker.Data;
using TreeSizeTracker.Models;

namespace TreeSizeTracker.Services;

public class FileScannerService
{
    private readonly TreeSizeDbContextFactory _dbContextFactory;
    private readonly ConfigurationService _configService;
    private readonly ILogger<FileScannerService> _logger;
    private List<ExclusionRule> _activeExclusions = new();
    private List<InclusionOverride> _activeInclusions = new();
    private HashSet<string> _scannedPaths = new();
    private Dictionary<string, int> _inclusionDepthOverrides = new();
    private ScanProgress _currentProgress = new();

    public FileScannerService(
        TreeSizeDbContextFactory dbContextFactory, 
        ConfigurationService configService,
        ILogger<FileScannerService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _configService = configService;
        _logger = logger;
    }

    public ScanProgress CurrentProgress => _currentProgress;

    public async Task<List<FolderSizeData>> PerformScanAsync(string? partitionPath = null)
    {
        // Reset progress
        _currentProgress = new ScanProgress
        {
            StartTime = DateTime.UtcNow,
            IsScanning = true,
            CurrentPartition = partitionPath,
            DirectoriesScanned = 0
        };

        var globalConfig = _configService.GetGlobalConfiguration();
        var scanResults = new List<FolderSizeData>();
        var scanTime = DateTime.UtcNow;

        // Get partitions to scan
        var partitionsToScan = new List<ScanConfiguration>();
        
        if (!string.IsNullOrEmpty(partitionPath))
        {
            // Scan specific partition
            var config = _configService.GetPartitionConfiguration(partitionPath);
            if (config.IsEnabled)
            {
                partitionsToScan.Add(config);
            }
        }
        else
        {
            // Scan all enabled partitions
            foreach (var kvp in globalConfig.PartitionConfigurations)
            {
                if (kvp.Value.IsEnabled)
                {
                    partitionsToScan.Add(kvp.Value);
                }
            }
        }

        // Scan each partition
        foreach (var configuration in partitionsToScan)
        {
            var partitionResults = await ScanPartitionAsync(configuration, scanTime);
            scanResults.AddRange(partitionResults);
        }

        // Mark scanning as complete
        _currentProgress.IsScanning = false;

        return scanResults;
    }

    private async Task<List<FolderSizeData>> ScanPartitionAsync(ScanConfiguration configuration, DateTime scanTime)
    {
        var scanResults = new List<FolderSizeData>();
        const int batchSize = 1000; // Save to database every 1000 records
        
        // Create database context for this partition
        using var dbContext = _dbContextFactory.CreateContext(configuration.PartitionPath);
        
        // Clear the scanned paths set for this scan
        _scannedPaths.Clear();
        _inclusionDepthOverrides.Clear();
        
        // Get active exclusion rules
        _activeExclusions = configuration.ExclusionRules
            .Where(r => r.IsEnabled)
            .ToList();

        // Get active inclusion overrides
        _activeInclusions = configuration.InclusionOverrides
            .Where(i => i.IsEnabled)
            .ToList();

        // Build depth override dictionary for quick lookup
        foreach (var inclusion in _activeInclusions)
        {
            var normalizedPath = Path.GetFullPath(inclusion.Path);
            _inclusionDepthOverrides[normalizedPath] = inclusion.ScanDepth;
        }

        foreach (var rootFolder in configuration.RootFolders.Where(f => f.IsEnabled))
        {
            try
            {
                if (!Directory.Exists(rootFolder.Path))
                {
                    _logger.LogWarning("Root folder does not exist: {FolderPath}", rootFolder.Path);
                    continue;
                }

                var maxDepth = rootFolder.MaxDepth ?? configuration.DefaultScanDepth;
                
                // Check if there's an inclusion override for this root folder
                var normalizedRootPath = Path.GetFullPath(rootFolder.Path);
                if (_inclusionDepthOverrides.ContainsKey(normalizedRootPath))
                {
                    maxDepth = _inclusionDepthOverrides[normalizedRootPath];
                    _logger.LogDebug("Using inclusion override depth {Depth} for root folder {Path}", maxDepth, rootFolder.Path);
                }
                
                // Safety limit to prevent excessive recursion (but allow 0 for current directory only)
                if (maxDepth > 100)
                {
                    maxDepth = 100;
                    _logger.LogInformation("Limiting scan depth to 100 levels for safety");
                }
                var results = await ScanFolderRecursiveAsync(rootFolder.Path, scanTime, 0, maxDepth);
                scanResults.AddRange(results);
                
                // Save batch if we've accumulated enough records
                if (scanResults.Count >= batchSize)
                {
                    dbContext.FolderSizes.AddRange(scanResults);
                    await dbContext.SaveChangesAsync();
                    _logger.LogInformation("Saved batch of {Count} records to partition {Partition}", scanResults.Count, configuration.PartitionPath);
                    scanResults.Clear(); // Clear the list to free memory
                    
                    // Also periodically clear the scanned paths to avoid memory bloat
                    if (_scannedPaths.Count > 10000)
                    {
                        _logger.LogDebug("Clearing scanned paths cache to free memory");
                        _scannedPaths.Clear();
                    }
                    
                    // Hint to GC to collect freed memory
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scanning root folder: {FolderPath}", rootFolder.Path);
            }
        }

        // Save any remaining results
        if (scanResults.Any())
        {
            dbContext.FolderSizes.AddRange(scanResults);
            await dbContext.SaveChangesAsync();
            _logger.LogInformation("Saved final batch of {Count} records to partition {Partition}", scanResults.Count, configuration.PartitionPath);
        }

        return scanResults;
    }

    private async Task<List<FolderSizeData>> ScanFolderRecursiveAsync(
        string path, 
        DateTime scanTime, 
        int currentDepth, 
        int maxDepth)
    {
        var results = new List<FolderSizeData>();

        // Skip if already scanned (handles junction loops)
        var normalizedPath = Path.GetFullPath(path);
        if (_scannedPaths.Contains(normalizedPath))
        {
            return results;
        }
        _scannedPaths.Add(normalizedPath);

        // Check if this folder has an inclusion override
        var inclusionOverride = GetInclusionOverride(normalizedPath);
        
        // Check if this folder should be excluded (unless it has a force include override)
        if (ShouldExclude(path) && (inclusionOverride == null || !inclusionOverride.ForceInclude))
        {
            _logger.LogDebug("Excluded folder: {FolderPath}", path);
            return results;
        }

        // If we have an inclusion override, adjust the max depth for this path
        var effectiveMaxDepth = maxDepth;
        if (inclusionOverride != null && _inclusionDepthOverrides.ContainsKey(normalizedPath))
        {
            effectiveMaxDepth = currentDepth + _inclusionDepthOverrides[normalizedPath];
            _logger.LogDebug("Applying inclusion override for {Path} with depth {Depth}", path, _inclusionDepthOverrides[normalizedPath]);
        }

        try
        {
            // Update progress
            _currentProgress.DirectoriesScanned++;
            _currentProgress.CurrentDirectory = path;

            // Determine if we should store this entry in the database
            bool shouldStoreEntry = currentDepth <= effectiveMaxDepth;
            
            // Determine if we're at the depth limit (need to aggregate everything below)
            bool atDepthLimit = currentDepth == effectiveMaxDepth;
            
            long totalSize = 0;
            int totalFileCount = 0;
            int subfolderCount = 0;
            
            // Get immediate folder stats (files in current directory only)
            var immediateStats = await GetFolderSizeAsync(path);
            totalSize = immediateStats.sizeInBytes;
            totalFileCount = immediateStats.fileCount;
            subfolderCount = immediateStats.subfolderCount;
            
            // Process subdirectories
            if (atDepthLimit)
            {
                // We're at the depth limit - aggregate all subdirectories recursively
                var aggregatedStats = await GetFolderSizeRecursiveAsync(path);
                totalSize = aggregatedStats.sizeInBytes;
                totalFileCount = aggregatedStats.fileCount;
                subfolderCount = aggregatedStats.subfolderCount;
            }
            else
            {
                // Continue scanning subdirectories individually
                try
                {
                    var subdirectories = Directory.GetDirectories(path);
                    foreach (var subdir in subdirectories)
                    {
                        // Check for reparse points (junctions, symlinks)
                        var dirInfo = new DirectoryInfo(subdir);
                        if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            _logger.LogDebug("Skipping reparse point: {Path}", subdir);
                            continue;
                        }

                        // Recursively scan subdirectory
                        var subdirResults = await ScanFolderRecursiveAsync(
                            subdir, scanTime, currentDepth + 1, effectiveMaxDepth);
                        
                        // Add subdirectory results to our results
                        results.AddRange(subdirResults);
                        
                        // Add immediate subdirectory sizes to our total
                        var immediateChild = subdirResults.FirstOrDefault(r => r.Path == subdir);
                        if (immediateChild != null)
                        {
                            totalSize += immediateChild.SizeInBytes;
                            totalFileCount += immediateChild.FileCount;
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    _logger.LogDebug("Access denied to subfolders of: {FolderPath}", path);
                }
            }
            
            // Store entry if within depth limit
            if (shouldStoreEntry)
            {
                results.Add(new FolderSizeData
                {
                    Path = path,
                    SizeInBytes = totalSize,
                    FileCount = totalFileCount,
                    SubfolderCount = subfolderCount,
                    ScanDateTime = scanTime
                });
            }
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogWarning("Access denied to folder: {FolderPath}", path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning folder: {FolderPath}", path);
        }

        return results;
    }

    private bool ShouldExclude(string path)
    {
        var normalizedPath = Path.GetFullPath(path);
        var folderName = Path.GetFileName(path);

        foreach (var rule in _activeExclusions)
        {
            var normalizedPattern = Path.DirectorySeparatorChar == '/' 
                ? rule.Pattern.Replace('\\', '/')
                : rule.Pattern.Replace('/', '\\');

            var match = rule.Type switch
            {
                ExclusionType.Path => string.Equals(normalizedPath, 
                    normalizedPattern.Contains(':') || normalizedPattern.StartsWith('/') || normalizedPattern.StartsWith('\\')
                        ? Path.GetFullPath(normalizedPattern)
                        : normalizedPattern, 
                    StringComparison.OrdinalIgnoreCase),
                    
                ExclusionType.PathPrefix => normalizedPath.StartsWith(
                    normalizedPattern.Contains(':') || normalizedPattern.StartsWith('/') || normalizedPattern.StartsWith('\\')
                        ? Path.GetFullPath(normalizedPattern)
                        : normalizedPattern, 
                    StringComparison.OrdinalIgnoreCase),
                    
                ExclusionType.FolderName => string.Equals(folderName, 
                    rule.Pattern, 
                    StringComparison.OrdinalIgnoreCase),
                    
                ExclusionType.Wildcard => MatchWildcard(normalizedPath, rule.Pattern),
                
                ExclusionType.Regex => Regex.IsMatch(normalizedPath, rule.Pattern, RegexOptions.IgnoreCase),
                
                _ => false
            };

            if (match)
            {
                return true;
            }
        }

        return false;
    }

    private InclusionOverride? GetInclusionOverride(string path)
    {
        var normalizedPath = Path.GetFullPath(path);
        
        // Check for exact path match
        return _activeInclusions.FirstOrDefault(i => 
            string.Equals(Path.GetFullPath(i.Path), normalizedPath, StringComparison.OrdinalIgnoreCase));
    }

    private bool MatchWildcard(string input, string pattern)
    {
        // Convert wildcard pattern to regex
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        
        return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase);
    }

    private async Task<(long sizeInBytes, int fileCount, int subfolderCount)> GetFolderSizeAsync(string path)
    {
        return await Task.Run(() =>
        {
            long totalSize = 0;
            int fileCount = 0;
            int subfolderCount = 0;

            try
            {
                // Count files in current directory only (not recursive)
                var files = Directory.GetFiles(path);
                fileCount = files.Length;

                foreach (var file in files)
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        totalSize += fileInfo.Length;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Could not get size for file: {FilePath}", file);
                    }
                }

                // Count subdirectories
                try
                {
                    subfolderCount = Directory.GetDirectories(path).Length;
                }
                catch (UnauthorizedAccessException)
                {
                    // Can't access subdirectories
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error getting folder stats: {FolderPath}", path);
            }

            return (totalSize, fileCount, subfolderCount);
        });
    }

    private async Task<(long sizeInBytes, int fileCount, int subfolderCount)> GetFolderSizeRecursiveAsync(string path)
    {
        return await Task.Run(() =>
        {
            // Always use manual recursion to avoid loading all file paths into memory
            return GetFolderSizeRecursiveManual(path);
        });
    }

    private (long sizeInBytes, int fileCount, int subfolderCount) GetFolderSizeRecursiveManual(string path)
    {
        long totalSize = 0;
        int fileCount = 0;
        int subfolderCount = 0;
        var dirsToProcess = new Stack<string>();
        dirsToProcess.Push(path);

        while (dirsToProcess.Count > 0)
        {
            var currentDir = dirsToProcess.Pop();
            
            try
            {
                // Process files in current directory
                foreach (var file in Directory.GetFiles(currentDir))
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        totalSize += fileInfo.Length;
                        fileCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Could not get size for file: {FilePath}", file);
                    }
                }

                // Add subdirectories to process
                if (currentDir == path)
                {
                    // Count immediate subdirectories for the root path
                    var subdirs = Directory.GetDirectories(currentDir);
                    subfolderCount = subdirs.Length;
                    foreach (var subdir in subdirs)
                    {
                        // Skip excluded directories
                        if (ShouldExclude(subdir))
                        {
                            _logger.LogDebug("Excluding directory from aggregation: {Path}", subdir);
                            continue;
                        }
                        
                        var dirInfo = new DirectoryInfo(subdir);
                        if ((dirInfo.Attributes & FileAttributes.ReparsePoint) == 0)
                        {
                            dirsToProcess.Push(subdir);
                        }
                    }
                }
                else
                {
                    // Just add subdirectories to process
                    foreach (var subdir in Directory.GetDirectories(currentDir))
                    {
                        // Skip excluded directories
                        if (ShouldExclude(subdir))
                        {
                            _logger.LogDebug("Excluding directory from aggregation: {Path}", subdir);
                            continue;
                        }
                        
                        var dirInfo = new DirectoryInfo(subdir);
                        if ((dirInfo.Attributes & FileAttributes.ReparsePoint) == 0)
                        {
                            dirsToProcess.Push(subdir);
                        }
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                _logger.LogDebug("Access denied to directory: {Directory}", currentDir);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error processing directory: {Directory}", currentDir);
            }
        }

        return (totalSize, fileCount, subfolderCount);
    }

    public async Task<List<FolderSizeDiff>> GetLatestDiffsAsync()
    {
        var diffs = new List<FolderSizeDiff>();
        var globalConfig = _configService.GetGlobalConfiguration();
        
        // Iterate through all enabled partitions
        foreach (var kvp in globalConfig.PartitionConfigurations)
        {
            if (!kvp.Value.IsEnabled) continue;
            
            using var dbContext = _dbContextFactory.CreateContext(kvp.Key);
            
            // Get distinct paths for this partition
            var paths = await dbContext.FolderSizes
                .Select(f => f.Path)
                .Distinct()
                .ToListAsync();

            foreach (var path in paths)
            {
                // Get the two most recent scans for this path
                var recentScans = await dbContext.FolderSizes
                    .Where(f => f.Path == path)
                    .OrderByDescending(f => f.ScanDateTime)
                    .Take(2)
                    .ToListAsync();

                if (recentScans.Count == 2)
                {
                    diffs.Add(new FolderSizeDiff
                    {
                        Path = path,
                        CurrentSize = recentScans[0].SizeInBytes,
                        PreviousSize = recentScans[1].SizeInBytes,
                        CurrentScanDate = recentScans[0].ScanDateTime,
                        PreviousScanDate = recentScans[1].ScanDateTime
                    });
                }
            }
        }

        return diffs;
    }
}