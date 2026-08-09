using System;
using System.Collections.Generic;

namespace ace_run.Models;

public class WorkspaceConfig
{
    /// <summary>
    /// The shape this build writes. See <see cref="AppData.CurrentVersion"/>.
    /// v2 added <see cref="Settings"/>; a v1 file simply has no such key and
    /// System.Text.Json leaves the property initializer in place, so there is no
    /// migration step.
    /// </summary>
    public const int CurrentVersion = 2;

    /// <summary>Re-stamped by <see cref="Services.DataService.SaveConfig"/> on every write.</summary>
    public int Version { get; set; } = CurrentVersion;
    public List<WorkspaceInfo> Workspaces { get; set; } = new();
    public Guid ActiveWorkspaceId { get; set; }
    public WindowState? WindowState { get; set; }

    /// <summary>
    /// Application-level preferences. One instance, owned by MainWindow for the process's
    /// lifetime — SettingsWindow mutates this very object rather than a reloaded copy,
    /// because MainWindow writes the whole config back when it closes and would otherwise
    /// overwrite anything the settings window had saved.
    /// </summary>
    public AppSettings Settings { get; set; } = new();
}
