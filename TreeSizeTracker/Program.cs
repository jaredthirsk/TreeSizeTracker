using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using TreeSizeTracker.Components;
using TreeSizeTracker.Data;
using TreeSizeTracker.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add MudBlazor services
builder.Services.AddMudServices();

// Add Entity Framework with SQLite
builder.Services.AddDbContext<TreeSizeDbContext>(options =>
    options.UseSqlite($"Data Source={Path.Combine(builder.Environment.ContentRootPath, "treesize.db")}"));

// Add application services
builder.Services.AddSingleton<ConfigurationService>();
builder.Services.AddScoped<FileScannerService>();
builder.Services.AddScoped<ReportingService>();
builder.Services.AddScoped<PartitionService>();
builder.Services.AddScoped<FolderTreeService>();
builder.Services.AddScoped<DatabaseCleanupService>();
builder.Services.AddHostedService<ScheduledScanService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Ensure database is created and handle schema updates
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<TreeSizeDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    
    try
    {
        // For development: Check if we need to recreate the database
        var dbPath = Path.Combine(builder.Environment.ContentRootPath, "treesize.db");
        bool needsRecreation = false;
        
        if (File.Exists(dbPath))
        {
            try
            {
                // Try to query the database to check if schema is correct
                var test = dbContext.FolderSizes.Take(1).ToList();
            }
            catch (Exception)
            {
                needsRecreation = true;
                logger.LogWarning("Database schema appears to be outdated, will recreate");
            }
        }
        
        if (needsRecreation)
        {
            dbContext.Database.EnsureDeleted();
        }
        
        dbContext.Database.EnsureCreated();
        logger.LogInformation("Database ready");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error creating database");
    }
}

app.Run();
