using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using TreeSizeTracker.Data;
using TreeSizeTracker.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add MudBlazor services
builder.Services.AddMudServices();

// Add data directory service first and perform migration immediately
var dataDirectoryService = new DataDirectoryService(new Microsoft.Extensions.Logging.Abstractions.NullLogger<DataDirectoryService>());
dataDirectoryService.MigrateOldData(builder.Environment);
dataDirectoryService.CleanupSqliteTemporaryFiles();
builder.Services.AddSingleton(dataDirectoryService);

// Add Entity Framework factory for per-partition databases
builder.Services.AddSingleton<TreeSizeDbContextFactory>();

// Add application services
builder.Services.AddSingleton<ConfigurationService>();
builder.Services.AddScoped<AppStateService>();
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
app.MapRazorComponents<TreeSizeTracker.Components.App>()
    .AddInteractiveServerRenderMode();

// Data migration was already performed during service registration

// Add graceful shutdown handling for database connections
var applicationLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
applicationLifetime.ApplicationStopping.Register(() =>
{
    using var scope = app.Services.CreateScope();
    var dbContextFactory = scope.ServiceProvider.GetRequiredService<TreeSizeDbContextFactory>();
    dbContextFactory.Dispose();
});

app.Run();
