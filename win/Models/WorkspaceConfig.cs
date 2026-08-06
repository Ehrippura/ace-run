using System;
using System.Collections.Generic;

namespace ace_run.Models;

public class WorkspaceConfig
{
    /// <summary>The shape this build writes. See <see cref="AppData.CurrentVersion"/>.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Re-stamped by <see cref="Services.DataService.SaveConfig"/> on every write.</summary>
    public int Version { get; set; } = CurrentVersion;
    public List<WorkspaceInfo> Workspaces { get; set; } = new();
    public Guid ActiveWorkspaceId { get; set; }
    public WindowState? WindowState { get; set; }
}
