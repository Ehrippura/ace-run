using System;
using System.IO;

namespace ace_run.Services;

/// <summary>
/// Where AceRun's data lives, as data rather than as baked-in constants.
/// </summary>
/// <remarks>
/// The root is a constructor argument so a test can point a <see cref="DataStore"/> at a
/// temporary directory. It used to be a <c>static readonly</c> field resolved in a type
/// initializer that also created the directory, which made the whole persistence layer
/// untestable twice over: nothing could redirect it, and merely referencing the class wrote
/// to the real profile.
///
/// Nothing here touches the filesystem — these are string computations. Directory creation
/// belongs to the operations that actually write.
/// </remarks>
public sealed class AceRunPaths
{
    /// <summary><c>%LOCALAPPDATA%\AceRun</c> — what the app itself runs on.</summary>
    public static AceRunPaths Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AceRun"));

    public AceRunPaths(string root)
    {
        Root = root;
        LegacyAppsFile = Path.Combine(root, "apps.json");
        ConfigFile = Path.Combine(root, "config.json");
        WorkspacesDir = Path.Combine(root, "workspaces");
    }

    public string Root { get; }

    /// <summary>The pre-workspace format. Read once, at migration, then renamed to <c>.bak</c>.</summary>
    public string LegacyAppsFile { get; }

    public string ConfigFile { get; }
    public string WorkspacesDir { get; }

    public string WorkspaceFile(Guid id) => Path.Combine(WorkspacesDir, $"{id}.json");

    /// <summary>Where <see cref="LegacyAppsFile"/> is parked once migration has read it.</summary>
    public string LegacyBackupFile => LegacyAppsFile + ".bak";
}
