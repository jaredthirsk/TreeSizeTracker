using System.Runtime.InteropServices;

namespace TreeSizeTracker.Services;

public class DataDirectoryService
{
    private readonly ILogger<DataDirectoryService> _logger;
    private readonly string _baseDataDirectory;

    public DataDirectoryService(ILogger<DataDirectoryService> logger)
    {
        _logger = logger;
        _baseDataDirectory = GetBaseDataDirectory();
        EnsureDirectoriesExist();
    }

    public string BaseDataDirectory => _baseDataDirectory;
    public string ConfigFilePath => Path.Combine(_baseDataDirectory, "config.json");
    public string DataDirectory => Path.Combine(_baseDataDirectory, "data");
    public string ReportsDirectory => Path.Combine(_baseDataDirectory, "Reports");

    public string GetDatabasePath(string partitionPath)
    {
        // Create a safe filename from the partition path
        var safeFileName = GetSafeFileName(partitionPath);
        return Path.Combine(DataDirectory, $"{safeFileName}.db");
    }

    private string GetBaseDataDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "TreeSizeTracker");
        }
        else
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(homeDir, ".treesizetracker");
        }
    }

    private void EnsureDirectoriesExist()
    {
        try
        {
            Directory.CreateDirectory(_baseDataDirectory);
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(ReportsDirectory);
            
            _logger.LogInformation("Data directories created/verified at: {BaseDirectory}", _baseDataDirectory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create data directories at: {BaseDirectory}", _baseDataDirectory);
            throw;
        }
    }

    private string GetSafeFileName(string partitionPath)
    {
        // Convert partition path to safe filename
        // Windows: "C:" -> "C"
        // Linux: "/" -> "root", "/home" -> "home"
        
        var safeName = partitionPath.Replace(":", "")
                                  .Replace("\\", "_")
                                  .Replace("/", "_")
                                  .Trim('_');
        
        if (string.IsNullOrEmpty(safeName))
        {
            safeName = "root";
        }
        
        return safeName;
    }

    public void MigrateOldData(IWebHostEnvironment environment)
    {
        try
        {
            // Migrate old config file
            var oldConfigPath = Path.Combine(environment.ContentRootPath, "scan-config.json");
            if (File.Exists(oldConfigPath) && !File.Exists(ConfigFilePath))
            {
                File.Move(oldConfigPath, ConfigFilePath);
                _logger.LogInformation("Migrated config file from {Old} to {New}", oldConfigPath, ConfigFilePath);
            }

            // Migrate old database
            var oldDbPath = Path.Combine(environment.ContentRootPath, "treesize.db");
            if (File.Exists(oldDbPath))
            {
                var newDbPath = Path.Combine(DataDirectory, "migrated_data.db");
                if (!File.Exists(newDbPath))
                {
                    File.Move(oldDbPath, newDbPath);
                    _logger.LogInformation("Migrated database from {Old} to {New}", oldDbPath, newDbPath);
                }
            }

            // Migrate old reports
            var oldReportsDir = Path.Combine(environment.ContentRootPath, "Reports");
            if (Directory.Exists(oldReportsDir))
            {
                foreach (var file in Directory.GetFiles(oldReportsDir))
                {
                    var fileName = Path.GetFileName(file);
                    var newFilePath = Path.Combine(ReportsDirectory, fileName);
                    if (!File.Exists(newFilePath))
                    {
                        File.Move(file, newFilePath);
                    }
                }
                _logger.LogInformation("Migrated reports from {Old} to {New}", oldReportsDir, ReportsDirectory);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during data migration");
        }
    }

    public void CleanupSqliteTemporaryFiles()
    {
        try
        {
            var tempFiles = Directory.GetFiles(DataDirectory, "*.db-shm")
                .Concat(Directory.GetFiles(DataDirectory, "*.db-wal"))
                .ToList();

            foreach (var tempFile in tempFiles)
            {
                try
                {
                    File.Delete(tempFile);
                    _logger.LogInformation("Deleted SQLite temporary file: {File}", tempFile);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not delete SQLite temporary file: {File}", tempFile);
                }
            }

            // Also clean up any temporary files in the old location
            var oldTempFiles = Directory.GetFiles(Environment.CurrentDirectory, "*.db-shm")
                .Concat(Directory.GetFiles(Environment.CurrentDirectory, "*.db-wal"))
                .Where(f => Path.GetFileName(f).StartsWith("treesize"))
                .ToList();

            foreach (var tempFile in oldTempFiles)
            {
                try
                {
                    File.Delete(tempFile);
                    _logger.LogInformation("Deleted old SQLite temporary file: {File}", tempFile);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not delete old SQLite temporary file: {File}", tempFile);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error cleaning up SQLite temporary files");
        }
    }
}