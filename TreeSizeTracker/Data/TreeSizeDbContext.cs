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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FolderSizeData>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => new { e.Path, e.ScanDateTime });
        });
    }
}