namespace TreeSizeTracker.Models;

public class FolderTreeNode
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? OverrideDepth { get; set; }
    public bool IsExpanded { get; set; }
    public bool IsLoading { get; set; }
    public bool HasChildren { get; set; }
    public List<FolderTreeNode> Children { get; set; } = new();
    
    // Helper to get just the folder name from the path
    public string DisplayName => string.IsNullOrEmpty(Name) ? System.IO.Path.GetFileName(Path) ?? Path : Name;
}