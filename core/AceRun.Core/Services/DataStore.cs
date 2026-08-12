using System;
using System.IO;
using System.Text.Json;
using ace_run.Models;

namespace ace_run.Services;

/// <summary>
/// Reads and writes AceRun's JSON, against whatever root <see cref="AceRunPaths"/> it was
/// handed. <see cref="DataService"/> is the static facade the app calls; this is the instance
/// underneath it, and the seam a test uses to run the whole persistence layer — migration
/// included — inside a temporary directory.
/// </summary>
public sealed class DataStore
{
    /// <summary>The instance the app runs on, rooted at <c>%LOCALAPPDATA%\AceRun</c>.</summary>
    public static DataStore Default { get; } = new(AceRunPaths.Default);

    public DataStore(AceRunPaths paths) => Paths = paths;

    public AceRunPaths Paths { get; }

    // --- Parsing ---
    //
    // Split out from the file reads so the "unreadable file falls back to a default" rule can
    // be tested without a filesystem. A corrupt config is not an error the user can act on:
    // the app has to start regardless, and starting empty beats not starting.

    public static AppData ParseAppData(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new AppData();

        try
        {
            return JsonSerializer.Deserialize<AppData>(json, AceRunJson.Options) ?? new AppData();
        }
        catch
        {
            // Deliberately unfiltered, carried over verbatim from the code this replaced: the
            // app has to start whatever state the file is in, and there is no failure here the
            // user could act on. Narrowing it would turn an unreadable file into a crash on
            // launch, which is the one outcome worse than losing the file's contents.
            return new AppData();
        }
    }

    public static WorkspaceConfig ParseConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new WorkspaceConfig();

        try
        {
            return JsonSerializer.Deserialize<WorkspaceConfig>(json, AceRunJson.Options)
                   ?? new WorkspaceConfig();
        }
        catch
        {
            return new WorkspaceConfig();
        }
    }

    // --- Legacy load (used for migration) ---

    public AppData Load() => ReadOrDefault(Paths.LegacyAppsFile, ParseAppData);

    // --- Workspace config ---

    /// <summary>
    /// The config the app starts on. Guarantees a usable one: at least one workspace, with
    /// <see cref="WorkspaceConfig.ActiveWorkspaceId"/> naming one of them.
    /// </summary>
    /// <remarks>
    /// The repair pass is not belt-and-braces. <see cref="LoadConfig"/> answers an unreadable
    /// file with a fresh <see cref="WorkspaceConfig"/>, whose workspace list is <em>empty</em>,
    /// and every consumer of the result assumes at least one exists. Worse, the failure was
    /// silent: startup runs on a fire-and-forget task, so the exception went unobserved and the
    /// user got a window with an empty workspace picker, no error, and no way back — the
    /// workspace files were all still on disk, just unreachable, and migration never re-ran
    /// because config.json did exist.
    /// </remarks>
    public WorkspaceConfig MigrateOrInitialize()
    {
        Directory.CreateDirectory(Paths.WorkspacesDir);

        if (!File.Exists(Paths.ConfigFile))
            return MigrateFromAppsJson();

        var config = LoadConfig();

        // Written back, not just patched in memory: everything else that calls LoadConfig
        // (the settings language read, the manage-workspaces dialog) would otherwise keep
        // seeing the broken file.
        if (EnsureUsable(config))
            SaveConfig(config);

        return config;
    }

    /// <summary>
    /// Brings a config up to the minimum every consumer assumes.
    /// </summary>
    /// <returns>True when something had to be repaired, so the caller can save.</returns>
    /// <remarks>
    /// Repairing rather than refusing to start: the workspace files are untouched, so a user
    /// whose config was damaged gets a working app and can import their data back. Deleting
    /// nothing is the safe direction to fail in.
    /// </remarks>
    public static bool EnsureUsable(WorkspaceConfig config)
    {
        var repaired = false;

        if (config.Workspaces.Count == 0)
        {
            config.Workspaces.Add(NewDefaultWorkspace());
            repaired = true;
        }

        if (!config.Workspaces.Exists(w => w.Id == config.ActiveWorkspaceId))
        {
            config.ActiveWorkspaceId = config.Workspaces[0].Id;
            repaired = true;
        }

        return repaired;
    }

    /// <summary>
    /// The workspace a fresh install starts with, and what a repair falls back to.
    /// </summary>
    /// <remarks>
    /// The name is not localized. This layer has no access to the resource loader, and the
    /// workspace is renameable — unlike the defaults the dialogs apply, which the user never
    /// gets a chance to see before they are committed.
    /// </remarks>
    private static WorkspaceInfo NewDefaultWorkspace() => new() { Name = "Default" };

    private WorkspaceConfig MigrateFromAppsJson()
    {
        var ws = NewDefaultWorkspace();
        var config = new WorkspaceConfig
        {
            Workspaces = { ws },
            ActiveWorkspaceId = ws.Id
        };

        var hadLegacy = File.Exists(Paths.LegacyAppsFile);
        var appData = hadLegacy ? Load() : new AppData();
        ws.AppCount = appData.ItemCount;
        SaveWorkspace(ws.Id, appData);

        if (hadLegacy)
            File.Move(Paths.LegacyAppsFile, Paths.LegacyBackupFile, overwrite: true);

        SaveConfig(config);
        return config;
    }

    public WorkspaceConfig LoadConfig() => ReadOrDefault(Paths.ConfigFile, ParseConfig);

    public void SaveConfig(WorkspaceConfig config)
    {
        config.Version = WorkspaceConfig.CurrentVersion;
        Directory.CreateDirectory(Paths.Root);
        WriteAtomic(Paths.ConfigFile, JsonSerializer.Serialize(config, AceRunJson.Options));
    }

    public AppData LoadWorkspace(Guid id) => ReadOrDefault(Paths.WorkspaceFile(id), ParseAppData);

    public void SaveWorkspace(Guid id, AppData data)
    {
        data.Version = AppData.CurrentVersion;
        Directory.CreateDirectory(Paths.WorkspacesDir);
        WriteAtomic(Paths.WorkspaceFile(id), JsonSerializer.Serialize(data, AceRunJson.Options));
    }

    public void DeleteWorkspace(Guid id)
    {
        var path = Paths.WorkspaceFile(id);
        if (File.Exists(path))
            File.Delete(path);
    }

    /// <summary>
    /// Serializes a workspace for <c>.acerun</c> export. Exports go through here rather than
    /// calling <see cref="JsonSerializer"/> directly so they get the same version stamp as an
    /// ordinary save — the <see cref="AppData"/> handed in comes straight out of
    /// <see cref="LoadWorkspace"/> and still carries that file's old number.
    /// </summary>
    public static string SerializeExport(WorkspaceExport export)
    {
        export.AceRunVersion = WorkspaceExport.CurrentVersion;
        export.AppData.Version = AppData.CurrentVersion;
        return JsonSerializer.Serialize(export, AceRunJson.Options);
    }

    /// <summary>
    /// Writes beside the target and renames into place, so a reader never sees a half-written
    /// file.
    /// </summary>
    /// <remarks>
    /// <see cref="File.WriteAllText(string,string)"/> truncates first and fills after: a crash,
    /// a full disk or a power cut in that window leaves a truncated file, and for
    /// <c>config.json</c> that means the index to every workspace is gone while the workspace
    /// files themselves are still on disk. The icon cache — data that can always be extracted
    /// again — has had this protection since it was written; the files that cannot be
    /// regenerated did not.
    ///
    /// A leftover <c>.tmp</c> is inert: nothing enumerates the data directories, and every
    /// lookup here is by exact path.
    /// </remarks>
    private static void WriteAtomic(string path, string json)
    {
        var temp = path + ".tmp";

        try
        {
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            throw;
        }
    }

    /// <summary>
    /// Reads a file and parses it, falling back to a fresh instance for anything that goes
    /// wrong. IO failures are caught alongside parse failures on purpose: a locked or
    /// half-written file is no more actionable to the user than a malformed one.
    /// </summary>
    private static T ReadOrDefault<T>(string path, Func<string?, T> parse)
    {
        if (!File.Exists(path))
            return parse(null);

        try
        {
            return parse(File.ReadAllText(path));
        }
        catch
        {
            return parse(null);
        }
    }
}
