using Microsoft.EntityFrameworkCore;
using TreeSizeTracker.Models;

namespace TreeSizeTracker.Data;

public class TreeSizeDbContext : DbContext
{
    public TreeSizeDbContext(DbContextOptions<TreeSizeDbContext> options)
        : base(options)
    {
    }

    public DbSet<FolderSizeData> FolderSizes { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FolderSizeData>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => new { e.Path, e.ScanDateTime });
        });
    }

    public void ConfigureJournalMode()
    {
        try
        {
            // Set SQLite journal mode to DELETE to avoid WAL files
            Database.ExecuteSqlRaw("PRAGMA journal_mode=DELETE;");
        }
        catch (Exception)
        {
            // Ignore errors setting journal mode
        }
    }

    public override void Dispose()
    {
        try
        {
            // Ensure all changes are saved before disposing
            if (ChangeTracker.HasChanges())
            {
                SaveChanges();
            }
            
            // Close the connection explicitly
            Database.CloseConnection();
        }
        catch (Exception)
        {
            // Ignore errors during disposal
        }
        finally
        {
            base.Dispose();
        }
    }

    public override async ValueTask DisposeAsync()
    {
        try
        {
            // Ensure all changes are saved before disposing
            if (ChangeTracker.HasChanges())
            {
                await SaveChangesAsync();
            }
            
            // Close the connection explicitly
            await Database.CloseConnectionAsync();
        }
        catch (Exception)
        {
            // Ignore errors during disposal
        }
        finally
        {
            await base.DisposeAsync();
        }
    }
}