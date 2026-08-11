using System;
using System.Text.Json;
using ace_run.Models;

namespace ace_run.Services;

/// <summary>
/// Static entry point to persistence, delegating to <see cref="DataStore.Default"/>.
/// </summary>
/// <remarks>
/// The app has one data directory and one config, so a static call is the honest shape at the
/// call sites — this facade exists so those did not have to change when the implementation
/// grew a constructor. Anything that needs to choose its own root (a test, chiefly) uses
/// <see cref="DataStore"/> directly.
/// </remarks>
public static class DataService
{
    /// <inheritdoc cref="AceRunJson.Options"/>
    public static JsonSerializerOptions JsonOptions => AceRunJson.Options;

    public static WorkspaceConfig MigrateOrInitialize() => DataStore.Default.MigrateOrInitialize();

    public static WorkspaceConfig LoadConfig() => DataStore.Default.LoadConfig();

    public static void SaveConfig(WorkspaceConfig config) => DataStore.Default.SaveConfig(config);

    public static AppData LoadWorkspace(Guid id) => DataStore.Default.LoadWorkspace(id);

    public static void SaveWorkspace(Guid id, AppData data) => DataStore.Default.SaveWorkspace(id, data);

    public static void DeleteWorkspace(Guid id) => DataStore.Default.DeleteWorkspace(id);

    /// <inheritdoc cref="DataStore.SerializeExport"/>
    public static string SerializeExport(WorkspaceExport export) => DataStore.SerializeExport(export);
}
