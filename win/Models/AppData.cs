using System.Collections.Generic;

namespace ace_run.Models;

public class AppData
{
    public int Version { get; set; } = 2;
    public List<TreeItem> Items { get; set; } = new();
    public List<RecentLaunch> RecentLaunches { get; set; } = new();
}
