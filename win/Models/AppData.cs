using System.Collections.Generic;

namespace ace_run.Models;

public class AppData
{
    public int Version { get; set; } = 3;
    public List<AppItem> UngroupedItems { get; set; } = new();
    public List<FolderItem> Folders { get; set; } = new();
    public List<RecentLaunch> RecentLaunches { get; set; } = new();
    // Kept for JSON backward-compat; runtime code reads/writes WindowState via WorkspaceConfig.
    public WindowState? WindowState { get; set; }
}
