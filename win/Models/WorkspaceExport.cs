using System;

namespace ace_run.Models;

public class WorkspaceExport
{
    public int AceRunVersion { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
    public string? ColorTag { get; set; }
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
    public AppData AppData { get; set; } = new();
}
