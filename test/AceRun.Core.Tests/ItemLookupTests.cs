using System;
using System.Linq;
using ace_run.Automation;
using ace_run.Models;
using Xunit;

namespace ace_run.Tests;

/// <summary>
/// Name and id resolution for the automation interface. The rules are pinned here because the
/// caller is an agent passing names back, and the same resolution will later decide which
/// program gets launched — "close enough" is not an acceptable answer.
/// </summary>
public sealed class ItemLookupTests
{
    // --- Workspaces by id ---

    [Fact]
    public void An_id_finds_its_workspace()
    {
        var config = Config(out var work, out _);

        var result = ItemLookup.Workspace(config, work.Id.ToString());

        Assert.Equal(LookupOutcome.Found, result.Outcome);
        Assert.Same(work, result.Value);
    }

    [Fact]
    public void An_unknown_id_is_NotFound_and_never_falls_back_to_names()
    {
        var config = Config(out _, out _);

        var result = ItemLookup.Workspace(config, Guid.NewGuid().ToString());

        Assert.Equal(LookupOutcome.NotFound, result.Outcome);
        Assert.Empty(result.Candidates);
    }

    // --- Workspaces by name ---

    [Fact]
    public void A_name_match_ignores_case_and_surrounding_space()
    {
        var config = Config(out var work, out _);

        Assert.Same(work, ItemLookup.Workspace(config, "  wOrK  ").Value);
    }

    [Fact]
    public void An_exact_name_wins_over_a_longer_one_containing_it()
    {
        var config = new WorkspaceConfig();
        var exact = new WorkspaceInfo { Name = "Work" };
        config.Workspaces.Add(exact);
        config.Workspaces.Add(new WorkspaceInfo { Name = "Work (old)" });

        var result = ItemLookup.Workspace(config, "Work");

        Assert.Equal(LookupOutcome.Found, result.Outcome);
        Assert.Same(exact, result.Value);
    }

    [Fact]
    public void A_partial_name_matches_when_nothing_is_exact()
    {
        var config = Config(out _, out var games);

        Assert.Same(games, ItemLookup.Workspace(config, "ame").Value);
    }

    [Fact]
    public void Two_partial_matches_are_Ambiguous_and_list_both()
    {
        var config = new WorkspaceConfig();
        config.Workspaces.Add(new WorkspaceInfo { Name = "Work" });
        config.Workspaces.Add(new WorkspaceInfo { Name = "Work (old)" });

        var result = ItemLookup.Workspace(config, "wor");

        Assert.Equal(LookupOutcome.Ambiguous, result.Outcome);
        Assert.Null(result.Value);
        Assert.Equal(2, result.Candidates.Count);
        Assert.Contains("Work (old)", result.Candidates.Select(c => c.Name));
    }

    [Fact]
    public void Two_workspaces_with_the_same_name_are_Ambiguous()
    {
        // The manage dialog de-duplicates names, but an imported file can still produce this.
        var config = new WorkspaceConfig();
        config.Workspaces.Add(new WorkspaceInfo { Name = "Work" });
        config.Workspaces.Add(new WorkspaceInfo { Name = "work" });

        Assert.Equal(LookupOutcome.Ambiguous, ItemLookup.Workspace(config, "Work").Outcome);
    }

    [Fact]
    public void A_name_nothing_matches_is_NotFound()
    {
        var config = Config(out _, out _);

        Assert.Equal(LookupOutcome.NotFound, ItemLookup.Workspace(config, "nope").Outcome);
    }

    // --- The active workspace ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_key_means_the_active_workspace(string? key)
    {
        var config = Config(out _, out var games);
        config.ActiveWorkspaceId = games.Id;

        Assert.Same(games, ItemLookup.Workspace(config, key).Value);
    }

    [Fact]
    public void A_stale_active_id_falls_back_to_the_first_workspace()
    {
        // Repairing the index is DataStore.MigrateOrInitialize's job and it writes to disk,
        // which this side must not do. Answering with the first workspace beats refusing.
        var config = Config(out var work, out _);
        config.ActiveWorkspaceId = Guid.NewGuid();

        var result = ItemLookup.Workspace(config, null);

        Assert.Equal(LookupOutcome.Found, result.Outcome);
        Assert.Same(work, result.Value);
    }

    [Fact]
    public void An_empty_workspace_list_is_NotFound()
    {
        Assert.Equal(LookupOutcome.NotFound, ItemLookup.Workspace(new WorkspaceConfig(), null).Outcome);
    }

    // --- Folders ---

    [Fact]
    public void A_folder_resolves_by_id_and_by_name()
    {
        var data = new AppData();
        var tools = new FolderItem { DisplayName = "Tools" };
        data.Folders.Add(tools);

        Assert.Same(tools, ItemLookup.Folder(data, tools.Id.ToString()).Value);
        Assert.Same(tools, ItemLookup.Folder(data, "tool").Value);
    }

    // --- Tags ---

    [Fact]
    public void A_tag_resolves_by_id_and_by_name()
    {
        var data = new AppData();
        var done = new TagItem { Name = "Done" };
        data.Tags.Add(done);

        Assert.Same(done, ItemLookup.Tag(data, done.Id.ToString()).Value);
        Assert.Same(done, ItemLookup.Tag(data, "done").Value);
    }

    [Fact]
    public void A_tag_the_workspace_does_not_have_is_NotFound()
    {
        // Tags are per workspace, so this is the normal answer for a tag that lives in another
        // one — the caller is told where to look rather than given an empty item list.
        var data = new AppData();
        data.Tags.Add(new TagItem { Name = "Progress" });

        Assert.Equal(LookupOutcome.NotFound, ItemLookup.Tag(data, "Done").Outcome);
        Assert.Equal(LookupOutcome.NotFound, ItemLookup.Tag(data, null).Outcome);
    }

    [Fact]
    public void No_folder_key_is_NotFound_rather_than_a_default_folder()
    {
        // Unlike a workspace, "no folder given" has a meaning of its own at the call site —
        // the whole workspace — so there is nothing sensible to default to here.
        var data = new AppData();
        data.Folders.Add(new FolderItem { DisplayName = "Tools" });

        Assert.Equal(LookupOutcome.NotFound, ItemLookup.Folder(data, null).Outcome);
        Assert.Equal(LookupOutcome.NotFound, ItemLookup.Folder(data, " ").Outcome);
    }

    // --- Items ---

    [Fact]
    public void An_item_is_found_wherever_it_sits_in_the_workspace()
    {
        // Flat across ungrouped items and every folder, because the caller names an item, not
        // a place.
        var data = new AppData();
        data.UngroupedItems.Add(new AppItem { DisplayName = "Notepad" });
        var tools = new FolderItem { DisplayName = "Tools" };
        var hex = new AppItem { DisplayName = "Hex Editor" };
        tools.Children.Add(hex);
        data.Folders.Add(tools);

        Assert.Equal(hex.Id, ItemLookup.Item(data, hex.Id.ToString()).Value!.Id);
        Assert.Equal(hex.Id, ItemLookup.Item(data, "hex editor").Value!.Id);
        Assert.Equal(hex.Id, ItemLookup.Item(data, "hex").Value!.Id);
    }

    [Fact]
    public void Two_items_with_one_name_are_Ambiguous_and_both_ids_are_offered()
    {
        // Nothing stops duplicate item names, and a library grown by dropping files in has
        // them. This is the resolution where refusing to choose matters most: the next thing
        // that happens to a resolved item is that it gets launched.
        var data = new AppData();
        var first = new AppItem { DisplayName = "Setup" };
        var second = new AppItem { DisplayName = "Setup" };
        data.UngroupedItems.Add(first);
        var tools = new FolderItem { DisplayName = "Tools" };
        tools.Children.Add(second);
        data.Folders.Add(tools);

        var result = ItemLookup.Item(data, "Setup");

        Assert.Equal(LookupOutcome.Ambiguous, result.Outcome);
        Assert.Null(result.Value);
        Assert.Equal(
            new[] { first.Id, second.Id }.OrderBy(id => id),
            result.Candidates.Select(c => c.Id).OrderBy(id => id));
    }

    [Fact]
    public void An_item_whose_whole_name_is_another_prefix_stays_reachable()
    {
        var data = new AppData();
        data.UngroupedItems.Add(new AppItem { DisplayName = "Steam" });
        data.UngroupedItems.Add(new AppItem { DisplayName = "Steam (Beta)" });

        Assert.Equal(LookupOutcome.Found, ItemLookup.Item(data, "Steam").Outcome);
        Assert.Equal("Steam", ItemLookup.Item(data, "Steam").Value!.DisplayName);
    }

    [Fact]
    public void No_item_key_is_NotFound()
    {
        var data = new AppData();
        data.UngroupedItems.Add(new AppItem { DisplayName = "Notepad" });

        Assert.Equal(LookupOutcome.NotFound, ItemLookup.Item(data, null).Outcome);
        Assert.Equal(LookupOutcome.NotFound, ItemLookup.Item(data, " ").Outcome);
    }

    private static WorkspaceConfig Config(out WorkspaceInfo work, out WorkspaceInfo games)
    {
        work = new WorkspaceInfo { Name = "Work" };
        games = new WorkspaceInfo { Name = "Games" };

        var config = new WorkspaceConfig();
        config.Workspaces.Add(work);
        config.Workspaces.Add(games);
        config.ActiveWorkspaceId = work.Id;
        return config;
    }
}
