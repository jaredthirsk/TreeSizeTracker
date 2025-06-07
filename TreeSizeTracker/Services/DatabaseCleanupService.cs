using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using TreeSizeTracker.Data;
using TreeSizeTracker.Models;

namespace TreeSizeTracker.Services;

public class DatabaseCleanupService
{
    private readonly TreeSizeDbContext _dbContext;
    private readonly ConfigurationService _configService;
    private readonly ILogger<DatabaseCleanupService> _logger;

    public DatabaseCleanupService(
        TreeSizeDbContext dbContext,
        ConfigurationService configService,
        ILogger<DatabaseCleanupService> logger)
    {
        _dbContext = dbContext;
        _configService = configService;
        _logger = logger;
    }

    public async Task<CleanupResult> CleanupDatabaseAsync(string? partitionPath = null)
    {
        var result = new CleanupResult();
        var globalConfig = _configService.GetGlobalConfiguration();
        
        // Get partitions to clean
        var partitionsToClean = new List<ScanConfiguration>();
        
        if (!string.IsNullOrEmpty(partitionPath))
        {
            var config = _configService.GetPartitionConfiguration(partitionPath);
            if (config.IsEnabled)
            {
                partitionsToClean.Add(config);
            }
        }
        else
        {
            foreach (var kvp in globalConfig.PartitionConfigurations)
            {
                if (kvp.Value.IsEnabled)
                {
                    partitionsToClean.Add(kvp.Value);
                }
            }
        }

        // Process each partition
        foreach (var configuration in partitionsToClean)
        {
            await CleanupPartitionAsync(configuration, result);
        }

        // Save changes
        if (result.TotalEntriesRemoved > 0)
        {
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Database cleanup completed. Removed {Count} entries", result.TotalEntriesRemoved);
        }

        return result;
    }

    private async Task CleanupPartitionAsync(ScanConfiguration configuration, CleanupResult result)
    {
        // Build depth map for all paths based on current configuration
        var depthMap = new Dictionary<string, int>();
        
        // Process inclusion overrides
        foreach (var inclusion in configuration.InclusionOverrides.Where(i => i.IsEnabled))
        {
            var normalizedPath = Path.GetFullPath(inclusion.Path);
            depthMap[normalizedPath] = inclusion.ScanDepth;
        }

        // Process root folders
        foreach (var rootFolder in configuration.RootFolders.Where(f => f.IsEnabled))
        {
            var normalizedPath = Path.GetFullPath(rootFolder.Path);
            if (!depthMap.ContainsKey(normalizedPath))
            {
                depthMap[normalizedPath] = rootFolder.MaxDepth ?? configuration.DefaultScanDepth;
            }
        }

        // Get all database entries for paths under this partition
        var allEntries = await _dbContext.FolderSizes
            .Where(f => f.Path.StartsWith(configuration.PartitionPath))
            .ToListAsync();

        // Group by scan date to process each scan separately
        var scanGroups = allEntries.GroupBy(e => e.ScanDateTime).ToList();

        foreach (var scanGroup in scanGroups)
        {
            var entriesToRemove = new List<FolderSizeData>();
            var entriesToUpdate = new Dictionary<string, FolderSizeData>();

            foreach (var entry in scanGroup)
            {
                var shouldRemove = false;
                var currentDepth = 0;
                string? parentPathToUpdate = null;

                // Find the controlling path and depth for this entry
                foreach (var kvp in depthMap)
                {
                    if (entry.Path.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        // Calculate depth from the controlling path
                        var relativePath = entry.Path.Substring(kvp.Key.Length).TrimStart(Path.DirectorySeparatorChar);
                        if (string.IsNullOrEmpty(relativePath))
                        {
                            currentDepth = 0; // This is the controlling path itself
                        }
                        else
                        {
                            currentDepth = relativePath.Split(Path.DirectorySeparatorChar).Length;
                        }

                        // Check if this entry exceeds the allowed depth
                        if (currentDepth > kvp.Value)
                        {
                            shouldRemove = true;
                            
                            // Find the parent path at the maximum allowed depth
                            var pathParts = entry.Path.Split(Path.DirectorySeparatorChar);
                            var controllingParts = kvp.Key.Split(Path.DirectorySeparatorChar);
                            var maxDepthParts = controllingParts.Length + kvp.Value;
                            
                            if (maxDepthParts < pathParts.Length)
                            {
                                parentPathToUpdate = string.Join(Path.DirectorySeparatorChar, 
                                    pathParts.Take(maxDepthParts));
                            }
                        }
                        break;
                    }
                }

                if (shouldRemove)
                {
                    entriesToRemove.Add(entry);
                    result.EntriesRemovedByPath.Add(entry.Path);

                    // Roll up the size to the parent
                    if (!string.IsNullOrEmpty(parentPathToUpdate))
                    {
                        if (!entriesToUpdate.ContainsKey(parentPathToUpdate))
                        {
                            var parentEntry = scanGroup.FirstOrDefault(e => e.Path == parentPathToUpdate);
                            if (parentEntry != null)
                            {
                                entriesToUpdate[parentPathToUpdate] = parentEntry;
                            }
                        }
                    }
                }
            }

            // Remove entries that exceed depth
            if (entriesToRemove.Any())
            {
                _dbContext.FolderSizes.RemoveRange(entriesToRemove);
                result.TotalEntriesRemoved += entriesToRemove.Count;
                
                _logger.LogInformation("Removing {Count} entries from scan date {Date}", 
                    entriesToRemove.Count, scanGroup.Key);
            }
        }
    }
}

public class CleanupResult
{
    public int TotalEntriesRemoved { get; set; }
    public List<string> EntriesRemovedByPath { get; set; } = new List<string>();
}