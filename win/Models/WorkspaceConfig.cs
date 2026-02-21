using System;
using System.Collections.Generic;

namespace ace_run.Models;

public class WorkspaceConfig
{
    public int Version { get; set; } = 1;
    public List<WorkspaceInfo> Workspaces { get; set; } = new();
    public Guid ActiveWorkspaceId { get; set; }
    public Guid? DefaultWorkspaceId { get; set; }
    public WindowState? WindowState { get; set; }
}
