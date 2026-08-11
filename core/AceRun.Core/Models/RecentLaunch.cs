using System;

namespace ace_run.Models;

public class RecentLaunch
{
    public Guid AppId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
}
