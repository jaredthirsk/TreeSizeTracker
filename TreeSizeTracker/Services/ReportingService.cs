using System.Text;
using TreeSizeTracker.Models;

namespace TreeSizeTracker.Services;

public class ReportingService
{
    private readonly DataDirectoryService _dataDirectoryService;
    private readonly ILogger<ReportingService> _logger;

    public ReportingService(DataDirectoryService dataDirectoryService, ILogger<ReportingService> logger)
    {
        _dataDirectoryService = dataDirectoryService;
        _logger = logger;
    }

    public async Task GenerateReportsAsync(List<FolderSizeDiff> diffs)
    {
        var reportDate = DateTime.Now;
        var reportsDir = _dataDirectoryService.ReportsDirectory;

        // Generate both CSV and text reports
        await GenerateCsvReportAsync(diffs, reportDate, reportsDir);
        await GenerateTextReportAsync(diffs, reportDate, reportsDir);
    }

    private async Task GenerateCsvReportAsync(List<FolderSizeDiff> diffs, DateTime reportDate, string reportsDir)
    {
        var fileName = $"size-diff-{reportDate:yyyy-MM-dd-HHmmss}.csv";
        var filePath = Path.Combine(reportsDir, fileName);

        var csv = new StringBuilder();
        csv.AppendLine("Path,Previous Size (MB),Current Size (MB),Difference (MB),Percentage Change,Previous Scan Date,Current Scan Date");

        // Filter out zero changes and sort by absolute difference descending
        var significantDiffs = diffs
            .Where(d => d.SizeDifference != 0)
            .OrderByDescending(d => Math.Abs(d.SizeDifference));

        foreach (var diff in significantDiffs)
        {
            csv.AppendLine($"\"{diff.Path}\"," +
                          $"{diff.PreviousSize / (1024.0 * 1024.0):F2}," +
                          $"{diff.CurrentSize / (1024.0 * 1024.0):F2}," +
                          $"{diff.SizeDifference / (1024.0 * 1024.0):F2}," +
                          $"{diff.PercentageChange:F2}%," +
                          $"{diff.PreviousScanDate:yyyy-MM-dd HH:mm:ss}," +
                          $"{diff.CurrentScanDate:yyyy-MM-dd HH:mm:ss}");
        }

        await File.WriteAllTextAsync(filePath, csv.ToString());
        _logger.LogInformation("CSV report generated: {FilePath}", filePath);
    }

    private async Task GenerateTextReportAsync(List<FolderSizeDiff> diffs, DateTime reportDate, string reportsDir)
    {
        var fileName = $"size-diff-{reportDate:yyyy-MM-dd-HHmmss}.txt";
        var filePath = Path.Combine(reportsDir, fileName);

        var report = new StringBuilder();
        report.AppendLine($"Disk Space Usage Report - {reportDate:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine(new string('=', 80));
        report.AppendLine();

        // Filter out directories with no change
        var changedDirs = diffs.Where(d => d.SizeDifference != 0).ToList();
        
        // Summary section
        var totalPreviousSize = diffs.Sum(d => d.PreviousSize);
        var totalCurrentSize = diffs.Sum(d => d.CurrentSize);
        var totalDifference = totalCurrentSize - totalPreviousSize;

        report.AppendLine("SUMMARY");
        report.AppendLine(new string('-', 40));
        report.AppendLine($"Total Previous Size: {FormatBytes(totalPreviousSize)}");
        report.AppendLine($"Total Current Size:  {FormatBytes(totalCurrentSize)}");
        report.AppendLine($"Total Change:        {FormatBytes(Math.Abs(totalDifference))} " +
                         $"({(totalDifference >= 0 ? "+" : "-")}{Math.Abs(totalDifference / (double)totalPreviousSize * 100):F2}%)");
        report.AppendLine($"Directories with changes: {changedDirs.Count} out of {diffs.Count} scanned");
        report.AppendLine();

        // Folders with significant changes (> 5% or > 100MB)
        var significantChanges = changedDirs.Where(d => 
            Math.Abs(d.PercentageChange) > 5 || 
            Math.Abs(d.SizeDifference) > 100 * 1024 * 1024)
            .OrderByDescending(d => Math.Abs(d.SizeDifference))
            .ToList();

        if (significantChanges.Any())
        {
            report.AppendLine("SIGNIFICANT CHANGES");
            report.AppendLine(new string('-', 40));
            
            foreach (var diff in significantChanges)
            {
                report.AppendLine($"Folder: {diff.Path}");
                report.AppendLine($"  Previous: {FormatBytes(diff.PreviousSize)}");
                report.AppendLine($"  Current:  {FormatBytes(diff.CurrentSize)}");
                report.AppendLine($"  Change:   {FormatBytes(Math.Abs(diff.SizeDifference))} " +
                                 $"({(diff.SizeDifference >= 0 ? "+" : "-")}{Math.Abs(diff.PercentageChange):F2}%)");
                report.AppendLine();
            }
        }

        // All folders with changes detail
        report.AppendLine("ALL CHANGES (sorted by size difference)");
        report.AppendLine(new string('-', 40));
        
        foreach (var diff in changedDirs.OrderByDescending(d => Math.Abs(d.SizeDifference)))
        {
            report.AppendLine($"{diff.Path}");
            report.AppendLine($"  {FormatBytes(diff.PreviousSize)} -> {FormatBytes(diff.CurrentSize)} " +
                             $"({(diff.SizeDifference >= 0 ? "+" : "-")}{Math.Abs(diff.PercentageChange):F2}%)");
        }

        await File.WriteAllTextAsync(filePath, report.ToString());
        _logger.LogInformation("Text report generated: {FilePath}", filePath);
    }

    private string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }

        return $"{len:F2} {sizes[order]}";
    }
}