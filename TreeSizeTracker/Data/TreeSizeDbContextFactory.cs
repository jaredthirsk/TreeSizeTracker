using Microsoft.EntityFrameworkCore;
using TreeSizeTracker.Services;

namespace TreeSizeTracker.Data;

public class TreeSizeDbContextFactory : IDisposable
{
    private readonly DataDirectoryService _dataDirectoryService;
    private readonly ILogger<TreeSizeDbContextFactory> _logger;
    private readonly Dictionary<string, TreeSizeDbContext> _activeContexts = new();
    private readonly object _lock = new object();

    public TreeSizeDbContextFactory(
        DataDirectoryService dataDirectoryService,
        ILogger<TreeSizeDbContextFactory> logger)
    {
        _dataDirectoryService = dataDirectoryService;
        _logger = logger;
    }

    public TreeSizeDbContext CreateContext(string partitionPath)
    {
        var databasePath = _dataDirectoryService.GetDatabasePath(partitionPath);
        
        var options = new DbContextOptionsBuilder<TreeSizeDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        var context = new TreeSizeDbContext(options);
        
        // Ensure the database is created
        try
        {
            context.Database.EnsureCreated();
            
            // Configure journal mode after database creation
            context.ConfigureJournalMode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating database for partition {Partition} at {DatabasePath}", 
                partitionPath, databasePath);
            throw;
        }

        return context;
    }

    public async Task<TreeSizeDbContext> CreateContextAsync(string partitionPath)
    {
        var context = CreateContext(partitionPath);
        
        // For async creation, we can run additional setup if needed
        await Task.CompletedTask;
        
        return context;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var context in _activeContexts.Values)
            {
                try
                {
                    context.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disposing database context");
                }
            }
            _activeContexts.Clear();
        }
    }
}