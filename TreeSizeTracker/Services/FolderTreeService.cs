using System.Runtime.InteropServices;
using TreeSizeTracker.Models;

namespace TreeSizeTracker.Services;

public class FolderTreeService
{
    private readonly ILogger<FolderTreeService> _logger;
    private readonly ConfigurationService _configService;

    public FolderTreeService(ILogger<FolderTreeService> logger, ConfigurationService configService)
    {
        _logger = logger;
        _configService = configService;
    }

    public async Task<List<FolderTreeNode>> GetRootNodesAsync(string partitionPath)
    {
        var nodes = new List<FolderTreeNode>();
        
        try
        {
            // Get the partition configuration to check for existing inclusion overrides
            var config = _configService.GetPartitionConfiguration(partitionPath);
            var inclusionMap = config.InclusionOverrides
                .Where(i => i.IsEnabled)
                .ToDictionary(i => Path.GetFullPath(i.Path), i => i.ScanDepth);

            // Ensure partition path ends with directory separator for root drives
            var rootPath = partitionPath;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && 
                partitionPath.Length == 2 && partitionPath[1] == ':')
            {
                // Windows drive letter without backslash (e.g., "C:" -> "C:\")
                rootPath = partitionPath + Path.DirectorySeparatorChar;
            }

            // Start with the partition root
            if (Directory.Exists(rootPath))
            {
                var rootNode = new FolderTreeNode
                {
                    Path = rootPath,
                    Name = partitionPath,
                    HasChildren = await HasSubdirectoriesAsync(rootPath),
                    OverrideDepth = inclusionMap.ContainsKey(Path.GetFullPath(rootPath)) 
                        ? inclusionMap[Path.GetFullPath(rootPath)] 
                        : null
                };
                nodes.Add(rootNode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting root nodes for partition: {Partition}", partitionPath);
        }

        return nodes;
    }

    public async Task<List<FolderTreeNode>> GetChildNodesAsync(string parentPath, string partitionPath)
    {
        var nodes = new List<FolderTreeNode>();
        
        try
        {
            // Get the partition configuration to check for existing inclusion overrides
            var config = _configService.GetPartitionConfiguration(partitionPath);
            var inclusionMap = config.InclusionOverrides
                .Where(i => i.IsEnabled)
                .ToDictionary(i => Path.GetFullPath(i.Path), i => i.ScanDepth);

            var directories = await Task.Run(() => Directory.GetDirectories(parentPath));
            
            foreach (var dir in directories)
            {
                try
                {
                    var dirInfo = new DirectoryInfo(dir);
                    
                    // Skip hidden and system directories
                    if ((dirInfo.Attributes & FileAttributes.Hidden) != 0 ||
                        (dirInfo.Attributes & FileAttributes.System) != 0)
                    {
                        continue;
                    }

                    // Skip reparse points (junctions, symlinks)
                    if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    var node = new FolderTreeNode
                    {
                        Path = dir,
                        Name = dirInfo.Name,
                        HasChildren = await HasSubdirectoriesAsync(dir),
                        OverrideDepth = inclusionMap.ContainsKey(Path.GetFullPath(dir)) 
                            ? inclusionMap[Path.GetFullPath(dir)] 
                            : null
                    };
                    
                    nodes.Add(node);
                }
                catch (UnauthorizedAccessException)
                {
                    // Skip folders we can't access
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error processing directory: {Directory}", dir);
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogDebug("Access denied to directory: {Directory}", parentPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting child nodes for: {Path}", parentPath);
        }

        return nodes.OrderBy(n => n.Name).ToList();
    }

    private async Task<bool> HasSubdirectoriesAsync(string path)
    {
        try
        {
            return await Task.Run(() => Directory.GetDirectories(path).Any());
        }
        catch
        {
            return false;
        }
    }

    public async Task SetFolderDepthOverrideAsync(string partitionPath, string folderPath, int? depth)
    {
        var config = _configService.GetPartitionConfiguration(partitionPath);
        
        // Find existing override
        var existingOverride = config.InclusionOverrides
            .FirstOrDefault(i => string.Equals(Path.GetFullPath(i.Path), Path.GetFullPath(folderPath), StringComparison.OrdinalIgnoreCase));

        if (depth.HasValue)
        {
            if (existingOverride != null)
            {
                // Update existing override
                existingOverride.ScanDepth = depth.Value;
            }
            else
            {
                // Add new override
                config.InclusionOverrides.Add(new InclusionOverride
                {
                    Path = folderPath,
                    ScanDepth = depth.Value,
                    ForceInclude = false,
                    IsEnabled = true
                });
            }
        }
        else if (existingOverride != null)
        {
            // Remove override if depth is null
            config.InclusionOverrides.Remove(existingOverride);
        }

        // Save configuration
        await _configService.SavePartitionConfigurationAsync(partitionPath, config);
    }
}