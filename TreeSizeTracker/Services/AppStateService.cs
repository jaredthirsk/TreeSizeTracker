using TreeSizeTracker.Models;

namespace TreeSizeTracker.Services;

public class AppStateService
{
    private string? _selectedPartition;
    private List<PartitionInfo>? _partitions;
    
    public event Action? OnPartitionChanged;
    public event Action? OnPartitionsLoaded;
    
    public string? SelectedPartition 
    { 
        get => _selectedPartition;
        set
        {
            if (_selectedPartition != value)
            {
                _selectedPartition = value;
                OnPartitionChanged?.Invoke();
            }
        }
    }
    
    public List<PartitionInfo>? Partitions
    {
        get => _partitions;
        set
        {
            _partitions = value;
            OnPartitionsLoaded?.Invoke();
        }
    }
    
    public void SetPartitions(List<PartitionInfo> partitions)
    {
        Partitions = partitions;
        
        // Auto-select first partition if none selected
        if (string.IsNullOrEmpty(SelectedPartition) && partitions.Any())
        {
            SelectedPartition = partitions.First().Path;
        }
    }
}