using System.Collections.Generic;

namespace ace_run.Models;

public class AppData
{
    /// <summary>Documentation-only marker; nothing branches on it. v5 added AppItem.Kind.</summary>
    public int Version { get; set; } = 5;
    public List<TagItem> Tags { get; set; } = new();
    public List<AppItem> UngroupedItems { get; set; } = new();
    public List<FolderItem> Folders { get; set; } = new();
    public List<RecentLaunch> RecentLaunches { get; set; } = new();
}
