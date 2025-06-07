using System.Runtime.InteropServices;
using System.Text.Json;
using TreeSizeTracker.Models;

namespace TreeSizeTracker.Services;

public class ConfigurationService
{
    private readonly string _configPath;
    private GlobalConfiguration _configuration;
    private readonly ILogger<ConfigurationService> _logger;

    public ConfigurationService(IWebHostEnvironment environment, ILogger<ConfigurationService> logger)
    {
        _configPath = Path.Combine(environment.ContentRootPath, "scan-config.json");
        _logger = logger;
        _configuration = LoadConfiguration();
    }

    public GlobalConfiguration GetGlobalConfiguration() => _configuration;

    public ScanConfiguration GetPartitionConfiguration(string partitionPath)
    {
        if (_configuration.PartitionConfigurations.TryGetValue(partitionPath, out var config))
        {
            return config;
        }

        // Create default configuration for new partition
        var newConfig = CreateDefaultPartitionConfiguration(partitionPath);
        _configuration.PartitionConfigurations[partitionPath] = newConfig;
        _ = SaveConfigurationAsync(); // Save async without waiting
        return newConfig;
    }

    public async Task SaveConfigurationAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_configuration, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            await File.WriteAllTextAsync(_configPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving configuration");
        }
    }

    public async Task SavePartitionConfigurationAsync(string partitionPath, ScanConfiguration config)
    {
        _configuration.PartitionConfigurations[partitionPath] = config;
        await SaveConfigurationAsync();
    }

    private GlobalConfiguration LoadConfiguration()
    {
        if (File.Exists(_configPath))
        {
            try
            {
                var json = File.ReadAllText(_configPath);
                return JsonSerializer.Deserialize<GlobalConfiguration>(json) ?? GetDefaultGlobalConfiguration();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading configuration");
            }
        }
        
        return GetDefaultGlobalConfiguration();
    }

    private GlobalConfiguration GetDefaultGlobalConfiguration()
    {
        return new GlobalConfiguration
        {
            CronSchedule = "0 0 * * *", // Daily at midnight
            IsScheduledScanEnabled = true,
            PartitionConfigurations = new Dictionary<string, ScanConfiguration>()
        };
    }

    private ScanConfiguration CreateDefaultPartitionConfiguration(string partitionPath)
    {
        var config = new ScanConfiguration
        {
            PartitionPath = partitionPath,
            DefaultScanDepth = int.MaxValue,
            IsEnabled = true,
            RootFolders = new List<RootFolder>(),
            ExclusionRules = new List<ExclusionRule>(),
            InclusionOverrides = new List<InclusionOverride>()
        };

        // Add the partition itself as a root folder
        config.RootFolders.Add(new RootFolder 
        { 
            Path = partitionPath, 
            IsEnabled = true 
        });

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Add common Windows exclusions
            config.ExclusionRules.AddRange(new[]
            {
                new ExclusionRule 
                { 
                    Pattern = @"Windows\WinSxS",
                    Type = ExclusionType.PathPrefix,
                    Description = "Windows Side-by-Side assemblies (large, system managed)"
                },
                new ExclusionRule 
                { 
                    Pattern = @"Windows\Installer",
                    Type = ExclusionType.PathPrefix,
                    Description = "Windows Installer cache"
                },
                new ExclusionRule 
                { 
                    Pattern = @"$Recycle.Bin",
                    Type = ExclusionType.FolderName,
                    Description = "Recycle Bin"
                },
                new ExclusionRule 
                { 
                    Pattern = @"System Volume Information",
                    Type = ExclusionType.FolderName,
                    Description = "System restore points"
                },
                new ExclusionRule 
                { 
                    Pattern = @"pagefile.sys",
                    Type = ExclusionType.FolderName,
                    Description = "Page file"
                },
                new ExclusionRule 
                { 
                    Pattern = @"hiberfil.sys",
                    Type = ExclusionType.FolderName,
                    Description = "Hibernation file"
                },
                new ExclusionRule 
                { 
                    Pattern = @"AppData\Local\Temp",
                    Type = ExclusionType.PathPrefix,
                    Description = "User temp folders"
                },
                new ExclusionRule 
                { 
                    Pattern = @"node_modules",
                    Type = ExclusionType.FolderName,
                    Description = "Node.js dependencies"
                },
                new ExclusionRule 
                { 
                    Pattern = @".git",
                    Type = ExclusionType.FolderName,
                    Description = "Git repositories"
                }
            });

            // Add common inclusion overrides for Windows
            if (partitionPath.EndsWith("C:"))
            {
                config.InclusionOverrides.AddRange(new[]
                {
                    new InclusionOverride
                    {
                        Path = Path.Combine(partitionPath, "Program Files"),
                        ScanDepth = 1,
                        Description = "Program Files - one level deep to see each program's size",
                        IsEnabled = true
                    },
                    new InclusionOverride
                    {
                        Path = Path.Combine(partitionPath, "Program Files (x86)"),
                        ScanDepth = 1,
                        Description = "Program Files (x86) - one level deep to see each program's size",
                        IsEnabled = true
                    },
                    new InclusionOverride
                    {
                        Path = Path.Combine(partitionPath, "Users"),
                        ScanDepth = 2,
                        Description = "Users folder - two levels deep to see user folders and their main subdirectories",
                        IsEnabled = true
                    }
                });
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Add common Linux exclusions
            config.ExclusionRules.AddRange(new[]
            {
                new ExclusionRule 
                { 
                    Pattern = "/proc",
                    Type = ExclusionType.Path,
                    Description = "Process information pseudo-filesystem"
                },
                new ExclusionRule 
                { 
                    Pattern = "/sys",
                    Type = ExclusionType.Path,
                    Description = "Sysfs virtual filesystem"
                },
                new ExclusionRule 
                { 
                    Pattern = "/dev",
                    Type = ExclusionType.Path,
                    Description = "Device files"
                },
                new ExclusionRule 
                { 
                    Pattern = "/run",
                    Type = ExclusionType.Path,
                    Description = "Runtime data"
                },
                new ExclusionRule 
                { 
                    Pattern = "/tmp",
                    Type = ExclusionType.Path,
                    Description = "Temporary files"
                },
                new ExclusionRule 
                { 
                    Pattern = ".cache",
                    Type = ExclusionType.FolderName,
                    Description = "Cache directories"
                },
                new ExclusionRule 
                { 
                    Pattern = "lost+found",
                    Type = ExclusionType.FolderName,
                    Description = "Filesystem recovery directories"
                },
                new ExclusionRule 
                { 
                    Pattern = "node_modules",
                    Type = ExclusionType.FolderName,
                    Description = "Node.js dependencies"
                },
                new ExclusionRule 
                { 
                    Pattern = ".git",
                    Type = ExclusionType.FolderName,
                    Description = "Git repositories"
                }
            });

            // Add inclusion overrides for root partition
            if (partitionPath == "/")
            {
                config.InclusionOverrides.AddRange(new[]
                {
                    new InclusionOverride
                    {
                        Path = "/usr",
                        ScanDepth = 1,
                        Description = "usr folder - one level deep to see major subdirectories",
                        IsEnabled = true
                    },
                    new InclusionOverride
                    {
                        Path = "/opt",
                        ScanDepth = 1,
                        Description = "opt folder - one level deep to see installed applications",
                        IsEnabled = true
                    },
                    new InclusionOverride
                    {
                        Path = "/home",
                        ScanDepth = 2,
                        Description = "home folder - two levels deep to see user folders and their main subdirectories",
                        IsEnabled = true
                    }
                });
            }
        }

        return config;
    }
}