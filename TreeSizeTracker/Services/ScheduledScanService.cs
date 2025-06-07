using NCrontab;

namespace TreeSizeTracker.Services;

public class ScheduledScanService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ConfigurationService _configService;
    private readonly ILogger<ScheduledScanService> _logger;

    public ScheduledScanService(
        IServiceProvider serviceProvider,
        ConfigurationService configService,
        ILogger<ScheduledScanService> logger)
    {
        _serviceProvider = serviceProvider;
        _configService = configService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var config = _configService.GetGlobalConfiguration();
                
                if (!config.IsScheduledScanEnabled)
                {
                    // Check again in 1 minute
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                    continue;
                }

                var schedule = CrontabSchedule.Parse(config.CronSchedule);
                var nextRun = schedule.GetNextOccurrence(DateTime.Now);
                var delay = nextRun - DateTime.Now;

                _logger.LogInformation("Next scheduled scan at: {NextRun}", nextRun);

                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, stoppingToken);
                }

                if (!stoppingToken.IsCancellationRequested)
                {
                    await PerformScheduledScanAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in scheduled scan service");
                // Wait 5 minutes before retrying
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }

    private async Task PerformScheduledScanAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting scheduled scan at {Time}", DateTime.Now);

        using var scope = _serviceProvider.CreateScope();
        var scannerService = scope.ServiceProvider.GetRequiredService<FileScannerService>();
        var reportingService = scope.ServiceProvider.GetRequiredService<ReportingService>();

        try
        {
            // Perform the scan
            var scanResults = await scannerService.PerformScanAsync();
            _logger.LogInformation("Scan completed. Found {Count} folder entries", scanResults.Count);

            // Generate reports
            var diffs = await scannerService.GetLatestDiffsAsync();
            if (diffs.Any())
            {
                await reportingService.GenerateReportsAsync(diffs);
                _logger.LogInformation("Reports generated successfully");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during scheduled scan");
        }
    }
}