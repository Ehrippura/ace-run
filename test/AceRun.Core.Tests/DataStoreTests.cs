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
