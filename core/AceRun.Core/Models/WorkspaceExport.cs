using System;

namespace ace_run.Models;

public class WorkspaceExport
{
    /// <summary>The shape this build writes. See <see cref="AppData.CurrentVersion"/>.</summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// Re-stamped by <see cref="Services.DataService.SerializeExport"/> on every write, along
    /// with <see cref="AppData"/>.<see cref="AppData.Version"/> — which arrives straight from
    /// LoadWorkspace and would otherwise carry that file's old number into the export.
    /// </summary>
    public int AceRunVersion { get; set; } = CurrentVersion;
    public string Name { get; set; } = string.Empty;
    public string? ColorTag { get; set; }
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
    public AppData AppData { get; set; } = new();
}
