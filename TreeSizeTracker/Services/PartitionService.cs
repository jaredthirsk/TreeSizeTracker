using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace TreeSizeTracker.Services;

public class PartitionService
{
    private readonly ILogger<PartitionService> _logger;

    public PartitionService(ILogger<PartitionService> logger)
    {
        _logger = logger;
    }

    public async Task<List<PartitionInfo>> GetPartitionsAsync()
    {
        var partitions = new List<PartitionInfo>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Get Windows drives
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable))
                .ToList();

            foreach (var drive in drives)
            {
                try
                {
                    partitions.Add(new PartitionInfo
                    {
                        Path = drive.Name.TrimEnd('\\'),
                        Label = string.IsNullOrWhiteSpace(drive.VolumeLabel) 
                            ? $"Local Disk ({drive.Name.TrimEnd('\\')})" 
                            : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})",
                        TotalSize = drive.TotalSize,
                        AvailableSpace = drive.AvailableFreeSpace,
                        FileSystem = drive.DriveFormat,
                        DriveType = drive.DriveType.ToString()
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error getting drive info for {Drive}", drive.Name);
                }
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Parse mount output for Linux
            var mountOutput = await ExecuteCommandAsync("mount");
            var dfOutput = await ExecuteCommandAsync("df -B1"); // Get sizes in bytes

            // Parse df output into dictionary for quick lookup
            var sizeInfo = new Dictionary<string, (long total, long available)>();
            var dfLines = dfOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in dfLines.Skip(1)) // Skip header
            {
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 6)
                {
                    if (long.TryParse(parts[1], out var total) && long.TryParse(parts[3], out var available))
                    {
                        sizeInfo[parts[5]] = (total, available);
                    }
                }
            }

            // Parse mount output
            var mountLines = mountOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in mountLines)
            {
                // Parse lines like: /dev/sda1 on / type ext4 (rw,relatime)
                var match = Regex.Match(line, @"^(.+?)\s+on\s+(.+?)\s+type\s+(.+?)\s+\((.+?)\)");
                if (match.Success)
                {
                    var device = match.Groups[1].Value;
                    var mountPoint = match.Groups[2].Value;
                    var fsType = match.Groups[3].Value;
                    var options = match.Groups[4].Value;

                    // Skip virtual filesystems
                    if (IsVirtualFileSystem(fsType, device))
                        continue;

                    // Only include root and /mnt/* paths
                    if (mountPoint != "/" && !mountPoint.StartsWith("/mnt/") && !mountPoint.StartsWith("/media/"))
                        continue;

                    var label = GetPartitionLabel(mountPoint, device);
                    
                    // Get size info
                    sizeInfo.TryGetValue(mountPoint, out var sizes);

                    partitions.Add(new PartitionInfo
                    {
                        Path = mountPoint,
                        Label = label,
                        TotalSize = sizes.total,
                        AvailableSpace = sizes.available,
                        FileSystem = fsType,
                        DriveType = "Fixed",
                        Device = device
                    });
                }
            }
        }

        return partitions.OrderBy(p => p.Path).ToList();
    }

    private bool IsVirtualFileSystem(string fsType, string device)
    {
        var virtualFsTypes = new[] { "proc", "sysfs", "devpts", "tmpfs", "securityfs", 
            "cgroup", "cgroup2", "autofs", "mqueue", "hugetlbfs", "debugfs", "tracefs",
            "fusectl", "fuse.gvfsd-fuse", "fuse.snapfuse" };
        
        return virtualFsTypes.Contains(fsType) || 
               device.StartsWith("none") || 
               device.StartsWith("udev") ||
               device.StartsWith("tmpfs");
    }

    private string GetPartitionLabel(string mountPoint, string device)
    {
        if (mountPoint == "/")
            return "Root Filesystem (/)";
        
        if (mountPoint.StartsWith("/mnt/"))
        {
            var name = Path.GetFileName(mountPoint);
            return $"{name} ({mountPoint})";
        }

        if (mountPoint.StartsWith("/media/"))
        {
            var name = Path.GetFileName(mountPoint);
            return $"{name} ({mountPoint})";
        }

        return $"{Path.GetFileName(device)} ({mountPoint})";
    }

    private async Task<string> ExecuteCommandAsync(string command)
    {
        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"-c \"{command}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return output;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command: {Command}", command);
            return string.Empty;
        }
    }
}

public class PartitionInfo
{
    public string Path { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public long TotalSize { get; set; }
    public long AvailableSpace { get; set; }
    public long FreeSpace => AvailableSpace;
    public long UsedSpace => TotalSize - AvailableSpace;
    public double UsagePercentage => TotalSize > 0 ? (double)UsedSpace / TotalSize * 100 : 0;
    public double FreePercentage => TotalSize > 0 ? (double)AvailableSpace / TotalSize * 100 : 0;
    public string FileSystem { get; set; } = string.Empty;
    public string DriveType { get; set; } = string.Empty;
    public string Device { get; set; } = string.Empty;
}