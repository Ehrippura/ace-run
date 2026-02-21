using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using ace_run.Models;

namespace ace_run.Services;

public static class DataService
{
    private static readonly string _dataDir;
    private static readonly string _legacyAppsPath;
    private static readonly string _configPath;
    private static readonly string _workspacesDir;

    private static readonly JsonSerializerOptions s_options = new()
    {
        WriteIndented = true
    };

    static DataService()
    {
        _dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AceRun");
        Directory.CreateDirectory(_dataDir);
        _legacyAppsPath = Path.Combine(_dataDir, "apps.json");
        _configPath = Path.Combine(_dataDir, "config.json");
        _workspacesDir = Path.Combine(_dataDir, "workspaces");
    }

    // --- Legacy load (used for migration) ---

    public static AppData Load()
    {
        if (!File.Exists(_legacyAppsPath))
            return new AppData();

        var json = File.ReadAllText(_legacyAppsPath);
        if (string.IsNullOrWhiteSpace(json))
            return new AppData();

        try
        {
            var data = JsonSerializer.Deserialize<AppData>(json, s_options);
            if (data is not null)
                return data;
        }
        catch { }

        return new AppData();
    }

    // --- Workspace config ---

    public static WorkspaceConfig MigrateOrInitialize()
    {
        Directory.CreateDirectory(_workspacesDir);

        if (!File.Exists(_configPath))
            return MigrateFromAppsJson();

        return LoadConfig();
    }

    private static WorkspaceConfig MigrateFromAppsJson()
    {
        var ws = new WorkspaceInfo { Name = "Default" };
        var config = new WorkspaceConfig
        {
            Workspaces = { ws },
            ActiveWorkspaceId = ws.Id,
            DefaultWorkspaceId = ws.Id
        };

        var appData = File.Exists(_legacyAppsPath) ? Load() : new AppData();
        ws.AppCount = appData.UngroupedItems.Count + appData.Folders.Sum(f => f.Children.Count);
        config.WindowState = appData.WindowState;

        SaveWorkspace(ws.Id, appData);

        if (File.Exists(_legacyAppsPath))
            File.Move(_legacyAppsPath, _legacyAppsPath + ".bak", overwrite: true);

        SaveConfig(config);
        return config;
    }

    public static WorkspaceConfig LoadConfig()
    {
        if (!File.Exists(_configPath))
            return new WorkspaceConfig();

        try
        {
            var json = File.ReadAllText(_configPath);
            var config = JsonSerializer.Deserialize<WorkspaceConfig>(json, s_options);
            if (config is not null)
                return config;
        }
        catch { }

        return new WorkspaceConfig();
    }

    public static void SaveConfig(WorkspaceConfig config)
    {
        var json = JsonSerializer.Serialize(config, s_options);
        File.WriteAllText(_configPath, json);
    }

    public static AppData LoadWorkspace(Guid id)
    {
        var path = Path.Combine(_workspacesDir, $"{id}.json");
        if (!File.Exists(path))
            return new AppData();

        try
        {
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<AppData>(json, s_options);
            if (data is not null)
                return data;
        }
        catch { }

        return new AppData();
    }

    public static void SaveWorkspace(Guid id, AppData data)
    {
        Directory.CreateDirectory(_workspacesDir);
        var path = Path.Combine(_workspacesDir, $"{id}.json");
        var json = JsonSerializer.Serialize(data, s_options);
        File.WriteAllText(path, json);
    }

    public static void DeleteWorkspace(Guid id)
    {
        var path = Path.Combine(_workspacesDir, $"{id}.json");
        if (File.Exists(path))
            File.Delete(path);
    }
}
