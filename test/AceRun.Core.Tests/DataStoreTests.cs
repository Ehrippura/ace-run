using System;
using System.IO;
using System.Linq;
using ace_run.Models;
using ace_run.Services;
using Xunit;

namespace ace_run.Tests;

/// <summary>
/// Exercises the persistence layer against a throwaway directory. Every test gets its own
/// root, which is the whole reason <see cref="AceRunPaths"/> takes one — before that, this
/// file could not have existed.
/// </summary>
public sealed class DataStoreTests : IDisposable
{
    private readonly string _root;
    private readonly DataStore _store;

    public DataStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "AceRunTests", Guid.NewGuid().ToString("N"));
        _store = new DataStore(new AceRunPaths(_root));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* a leftover temp dir is not worth failing a test over */ }
    }

    // --- Construction is inert ---

    [Fact]
    public void Constructing_a_store_touches_nothing_on_disk()
    {
        // The bug this pins: the old static class resolved its paths and created its data
        // directory in a type initializer, so merely referencing it wrote to the profile.
        Assert.False(Directory.Exists(_root));
    }

    // --- Fresh install ---

    [Fact]
    public void MigrateOrInitialize_on_a_clean_root_creates_one_default_workspace()
    {
        var config = _store.MigrateOrInitialize();

        var ws = Assert.Single(config.Workspaces);
        Assert.Equal("Default", ws.Name);
        Assert.Equal(ws.Id, config.ActiveWorkspaceId);
        Assert.Equal(0, ws.AppCount);
        Assert.True(File.Exists(_store.Paths.ConfigFile));
        Assert.True(File.Exists(_store.Paths.WorkspaceFile(ws.Id)));
    }

    [Fact]
    public void MigrateOrInitialize_is_a_plain_load_once_a_config_exists()
    {
        var first = _store.MigrateOrInitialize();
        var second = _store.MigrateOrInitialize();

        // Same workspace id: a second run must not mint another "Default".
        Assert.Equal(first.Workspaces[0].Id, Assert.Single(second.Workspaces).Id);
    }

    // --- The usable-config floor ---
    //
    // Every consumer of the returned config assumes at least one workspace exists and that
    // ActiveWorkspaceId names one of them. LoadConfig cannot promise that — an unreadable file
    // yields a WorkspaceConfig with an empty list — and the failure was silent, because startup
    // runs on a fire-and-forget task: the user got a window with an empty workspace picker, no
    // error, and no way back.

    [Fact]
    public void A_corrupt_config_is_repaired_rather_than_handed_over_empty()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(_store.Paths.ConfigFile, "}}not json{{");

        var config = _store.MigrateOrInitialize();

        var ws = Assert.Single(config.Workspaces);
        Assert.Equal(ws.Id, config.ActiveWorkspaceId);
    }

    [Fact]
    public void The_repair_is_written_back_to_disk()
    {
        // Otherwise everything else that calls LoadConfig — the language read at startup, the
        // manage-workspaces dialog — would keep seeing the broken file.
        Directory.CreateDirectory(_root);
        File.WriteAllText(_store.Paths.ConfigFile, "not json");

        var repaired = _store.MigrateOrInitialize();
        var reloaded = _store.LoadConfig();

        Assert.Equal(repaired.Workspaces[0].Id, Assert.Single(reloaded.Workspaces).Id);
    }

    [Fact]
    public void A_config_whose_workspace_list_is_empty_is_repaired()
    {
        // Valid JSON, valid shape, unusable content — the case a parse check cannot catch.
        Directory.CreateDirectory(_root);
        File.WriteAllText(_store.Paths.ConfigFile, """{ "Version": 2, "Workspaces": [] }""");

        Assert.Single(_store.MigrateOrInitialize().Workspaces);
    }

    [Fact]
    public void A_repair_keeps_the_existing_workspaces()
    {
        // Only the active pointer is wrong here. Minting a new workspace would orphan the two
        // that are already there.
        var config = new WorkspaceConfig
        {
            Workspaces = { new WorkspaceInfo { Name = "One" }, new WorkspaceInfo { Name = "Two" } },
            ActiveWorkspaceId = Guid.NewGuid() // points at nothing
        };
        _store.SaveConfig(config);

        var loaded = _store.MigrateOrInitialize();

        Assert.Equal(2, loaded.Workspaces.Count);
        Assert.Equal(loaded.Workspaces[0].Id, loaded.ActiveWorkspaceId);
    }

    [Fact]
    public void Migration_leaves_existing_workspace_files_alone()
    {
        // The safe direction to fail in: a user whose config was damaged gets a working app and
        // can import their data back, because nothing deleted it.
        var orphanId = Guid.NewGuid();
        _store.SaveWorkspace(orphanId, new AppData { UngroupedItems = { new AppItem { DisplayName = "Kept" } } });
        File.WriteAllText(_store.Paths.ConfigFile, "corrupt");

        _store.MigrateOrInitialize();

        Assert.Equal("Kept", Assert.Single(_store.LoadWorkspace(orphanId).UngroupedItems).DisplayName);
    }

    // --- EnsureUsable, directly ---

    [Fact]
    public void EnsureUsable_reports_no_change_for_a_healthy_config()
    {
        var config = new WorkspaceConfig { Workspaces = { new WorkspaceInfo { Name = "One" } } };
        config.ActiveWorkspaceId = config.Workspaces[0].Id;

        Assert.False(DataStore.EnsureUsable(config));
    }

    [Fact]
    public void EnsureUsable_adds_a_workspace_when_there_are_none()
    {
        var config = new WorkspaceConfig();

        Assert.True(DataStore.EnsureUsable(config));
        Assert.Equal(Assert.Single(config.Workspaces).Id, config.ActiveWorkspaceId);
    }

    [Fact]
    public void EnsureUsable_repoints_a_dangling_active_id()
    {
        var config = new WorkspaceConfig
        {
            Workspaces = { new WorkspaceInfo { Name = "One" } },
            ActiveWorkspaceId = Guid.NewGuid()
        };

        Assert.True(DataStore.EnsureUsable(config));
        Assert.Equal(config.Workspaces[0].Id, config.ActiveWorkspaceId);
    }

    [Fact]
    public void EnsureUsable_is_idempotent()
    {
        var config = new WorkspaceConfig();

        DataStore.EnsureUsable(config);

        Assert.False(DataStore.EnsureUsable(config));
    }

    // --- Atomic writes ---

    [Fact]
    public void A_save_leaves_no_temp_file_behind()
    {
        // Writes go beside the target and are renamed into place: File.WriteAllText truncates
        // first and fills after, and a crash in that window would leave config.json truncated —
        // the index to every workspace gone while the workspace files themselves survive.
        _store.SaveConfig(new WorkspaceConfig());
        _store.SaveWorkspace(Guid.NewGuid(), new AppData());

        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void Overwriting_an_existing_file_works()
    {
        // File.Move needs overwrite:true for the second save onwards; without it every save
        // after the first would throw.
        var id = Guid.NewGuid();
        _store.SaveWorkspace(id, new AppData { UngroupedItems = { new AppItem { DisplayName = "First" } } });
        _store.SaveWorkspace(id, new AppData { UngroupedItems = { new AppItem { DisplayName = "Second" } } });

        Assert.Equal("Second", Assert.Single(_store.LoadWorkspace(id).UngroupedItems).DisplayName);
    }

    // --- Migration from the pre-workspace format ---

    [Fact]
    public void MigrateOrInitialize_moves_apps_json_into_a_workspace_and_backs_it_up()
    {
        var legacy = new AppData
        {
            UngroupedItems = { new AppItem { DisplayName = "Loose" } },
            Folders =
            {
                new FolderItem
                {
                    DisplayName = "Games",
                    Children = { new AppItem { DisplayName = "A" }, new AppItem { DisplayName = "B" } }
                }
            }
        };
        WriteLegacy(legacy);

        var config = _store.MigrateOrInitialize();

        var ws = Assert.Single(config.Workspaces);
        // AppCount counts foldered items too — three, not one.
        Assert.Equal(3, ws.AppCount);

        var migrated = _store.LoadWorkspace(ws.Id);
        Assert.Equal("Loose", Assert.Single(migrated.UngroupedItems).DisplayName);
        Assert.Equal("Games", Assert.Single(migrated.Folders).DisplayName);
        Assert.Equal(2, migrated.Folders[0].Children.Count);

        // The original is renamed rather than deleted, and is gone from its old name so a
        // second launch does not migrate it twice.
        Assert.False(File.Exists(_store.Paths.LegacyAppsFile));
        Assert.True(File.Exists(_store.Paths.LegacyBackupFile));
    }

    [Fact]
    public void Migration_preserves_item_ids()
    {
        // Icon cache entries are keyed by AppItem.Id, so a migration that reissued ids would
        // silently orphan every cached icon.
        var id = Guid.NewGuid();
        WriteLegacy(new AppData { UngroupedItems = { new AppItem { Id = id, DisplayName = "Keep" } } });

        var config = _store.MigrateOrInitialize();
        var migrated = _store.LoadWorkspace(config.Workspaces[0].Id);

        Assert.Equal(id, Assert.Single(migrated.UngroupedItems).Id);
    }

    [Fact]
    public void Migration_of_an_unreadable_apps_json_still_produces_a_usable_config()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(_store.Paths.LegacyAppsFile, "{ this is not json");

        var config = _store.MigrateOrInitialize();

        Assert.Single(config.Workspaces);
        Assert.Equal(0, config.Workspaces[0].AppCount);
        // Still backed up: the file is unreadable to us, but it is the user's only copy.
        Assert.True(File.Exists(_store.Paths.LegacyBackupFile));
    }

    // --- Round trips ---

    [Fact]
    public void Workspace_round_trips_through_disk()
    {
        var tagId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var original = new AppData
        {
            Tags = { new TagItem { Id = tagId, Name = "Work", ColorKey = "Green" } },
            UngroupedItems =
            {
                new AppItem
                {
                    Kind = ItemKind.Url,
                    DisplayName = "Docs",
                    FilePath = "https://example.com",
                    SortKey = "a1",
                    TagIds = { tagId }
                }
            },
            RecentLaunches = { new RecentLaunch { AppId = id, DisplayName = "Docs" } }
        };

        _store.SaveWorkspace(id, original);
        var loaded = _store.LoadWorkspace(id);

        var item = Assert.Single(loaded.UngroupedItems);
        Assert.Equal(ItemKind.Url, item.Kind);
        Assert.Equal("Docs", item.DisplayName);
        Assert.Equal("https://example.com", item.FilePath);
        Assert.Equal("a1", item.SortKey);
        Assert.Equal(tagId, Assert.Single(item.TagIds));
        Assert.Equal("Green", Assert.Single(loaded.Tags).ColorKey);
        Assert.Equal(id, Assert.Single(loaded.RecentLaunches).AppId);
    }

    [Fact]
    public void Saving_stamps_the_current_version_whatever_the_instance_carried()
    {
        var id = Guid.NewGuid();
        _store.SaveWorkspace(id, new AppData { Version = 1 });

        Assert.Equal(AppData.CurrentVersion, _store.LoadWorkspace(id).Version);
    }

    [Fact]
    public void Enums_persist_as_names_not_numbers()
    {
        // The file is meant to be readable and hand-editable; a numeric enum would also break
        // the moment a member was inserted rather than appended.
        var id = Guid.NewGuid();
        _store.SaveWorkspace(id, new AppData
        {
            UngroupedItems = { new AppItem { Kind = ItemKind.Url } }
        });

        var json = File.ReadAllText(_store.Paths.WorkspaceFile(id));
        Assert.Contains("\"Url\"", json);
    }

    [Fact]
    public void Config_round_trips_including_settings()
    {
        var config = new WorkspaceConfig
        {
            Workspaces = { new WorkspaceInfo { Name = "Main", ColorTag = "Blue" } }
        };
        config.ActiveWorkspaceId = config.Workspaces[0].Id;
        config.Settings.Theme = AppTheme.Dark;
        config.Settings.CloseToTray = false;

        _store.SaveConfig(config);
        var loaded = _store.LoadConfig();

        Assert.Equal("Main", Assert.Single(loaded.Workspaces).Name);
        Assert.Equal("Blue", loaded.Workspaces[0].ColorTag);
        Assert.Equal(config.ActiveWorkspaceId, loaded.ActiveWorkspaceId);
        Assert.Equal(AppTheme.Dark, loaded.Settings.Theme);
        Assert.False(loaded.Settings.CloseToTray);
    }

    // --- Missing and malformed files ---

    [Fact]
    public void Loading_an_absent_workspace_yields_an_empty_one()
    {
        var data = _store.LoadWorkspace(Guid.NewGuid());

        Assert.Empty(data.UngroupedItems);
        Assert.Empty(data.Folders);
        Assert.Equal(AppData.CurrentVersion, data.Version);
    }

    [Fact]
    public void Loading_a_corrupt_config_yields_a_default_rather_than_throwing()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(_store.Paths.ConfigFile, "]]not json[[");

        var loaded = _store.LoadConfig();

        Assert.Empty(loaded.Workspaces);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ broken")]
    [InlineData("[1,2,3]")]
    public void ParseAppData_never_returns_null(string? json)
        => Assert.NotNull(DataStore.ParseAppData(json));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json at all")]
    public void ParseConfig_never_returns_null(string? json)
        => Assert.NotNull(DataStore.ParseConfig(json));

    [Fact]
    public void An_older_file_missing_a_field_keeps_the_property_initializer()
    {
        // This is why new fields need no migration code: System.Text.Json leaves the
        // initializer in place for a key that is not in the file.
        var data = DataStore.ParseAppData("""{ "Version": 3, "UngroupedItems": [] }""");

        Assert.NotNull(data.Tags);
        Assert.NotNull(data.Folders);
        Assert.NotNull(data.RecentLaunches);
        Assert.Equal(3, data.Version);
    }

    // --- Delete ---

    [Fact]
    public void DeleteWorkspace_removes_the_file_and_is_safe_to_repeat()
    {
        var id = Guid.NewGuid();
        _store.SaveWorkspace(id, new AppData());
        Assert.True(File.Exists(_store.Paths.WorkspaceFile(id)));

        _store.DeleteWorkspace(id);
        Assert.False(File.Exists(_store.Paths.WorkspaceFile(id)));

        _store.DeleteWorkspace(id); // absent file must not throw
    }

    // --- Export ---

    [Fact]
    public void SerializeExport_stamps_both_versions()
    {
        var export = new WorkspaceExport
        {
            AceRunVersion = 0,
            AppData = new AppData { Version = 2 },
            Name = "Exported"
        };

        var json = DataStore.SerializeExport(export);

        Assert.Equal(WorkspaceExport.CurrentVersion, export.AceRunVersion);
        Assert.Equal(AppData.CurrentVersion, export.AppData.Version);
        Assert.Contains("Exported", json);
    }

    private void WriteLegacy(AppData data)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(_store.Paths.LegacyAppsFile,
            System.Text.Json.JsonSerializer.Serialize(data, AceRunJson.Options));
    }
}
