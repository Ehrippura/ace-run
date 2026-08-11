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

    public WorkspaceConfig MigrateOrInitialize()
    {
        Directory.CreateDirectory(Paths.WorkspacesDir);

        return File.Exists(Paths.ConfigFile) ? LoadConfig() : MigrateFromAppsJson();
    }

    private WorkspaceConfig MigrateFromAppsJson()
    {
        var ws = new WorkspaceInfo { Name = "Default" };
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
        File.WriteAllText(Paths.ConfigFile, JsonSerializer.Serialize(config, AceRunJson.Options));
    }

    public AppData LoadWorkspace(Guid id) => ReadOrDefault(Paths.WorkspaceFile(id), ParseAppData);

    public void SaveWorkspace(Guid id, AppData data)
    {
        data.Version = AppData.CurrentVersion;
        Directory.CreateDirectory(Paths.WorkspacesDir);
        File.WriteAllText(Paths.WorkspaceFile(id), JsonSerializer.Serialize(data, AceRunJson.Options));
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
