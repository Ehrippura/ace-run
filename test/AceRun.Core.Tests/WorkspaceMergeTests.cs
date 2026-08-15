using System;
using System.Collections.Generic;
using System.Linq;
using ace_run.Models;
using ace_run.Services;
using Xunit;

namespace ace_run.Tests;

public class WorkspaceMergeTests
{
    private static AppItem Item(string name, params Guid[] tagIds) => new()
    {
        DisplayName = name,
        FilePath = $@"C:\{name}.exe",
        TagIds = tagIds.ToList()
    };

    private static FolderItem Folder(string name, params AppItem[] children) => new()
    {
        DisplayName = name,
        Children = children.ToList()
    };

    private static IEnumerable<string> Names(IEnumerable<AppItem> items) => items.Select(i => i.DisplayName);

    // --- Items ---

    [Fact]
    public void Ungrouped_items_are_appended_after_the_destination_s_own()
    {
        var target = new AppData { UngroupedItems = { Item("Mine") } };
        var incoming = new AppData { UngroupedItems = { Item("Theirs") } };

        var result = WorkspaceMerge.Merge(target, incoming);

        Assert.Equal(new[] { "Mine", "Theirs" }, Names(target.UngroupedItems));
        Assert.Equal(1, result.ItemsAdded);
    }

    [Fact]
    public void Imported_items_get_fresh_ids()
    {
        // Ids are not unique across files: exporting a workspace and merging it back hands us
        // ids the destination already uses, and the icon cache is keyed by id.
        var shared = Item("Shared");
        var target = new AppData { UngroupedItems = { shared } };
        var incoming = new AppData { UngroupedItems = { Item("Shared") } };
        incoming.UngroupedItems[0].Id = shared.Id;

        WorkspaceMerge.Merge(target, incoming);

        Assert.Equal(2, target.UngroupedItems.Count);
        Assert.NotEqual(target.UngroupedItems[0].Id, target.UngroupedItems[1].Id);
    }

    [Fact]
    public void Everything_that_defines_an_item_survives_the_copy()
    {
        var incoming = new AppData
        {
            UngroupedItems =
            {
                new AppItem
                {
                    Kind = ItemKind.Url,
                    DisplayName = "Docs",
                    FilePath = "https://example.com",
                    Arguments = "--flag",
                    WorkingDirectory = @"C:\work",
                    RunAsAdmin = true,
                    CustomIconPath = @"C:\icon.ico",
                    SortKey = "010"
                }
            }
        };

        var target = new AppData();
        WorkspaceMerge.Merge(target, incoming);

        var source = incoming.UngroupedItems[0];
        var merged = Assert.Single(target.UngroupedItems);

        Assert.Equal(ItemKind.Url, merged.Kind);
        Assert.Equal(source.DisplayName, merged.DisplayName);
        Assert.Equal(source.FilePath, merged.FilePath);
        Assert.Equal(source.Arguments, merged.Arguments);
        Assert.Equal(source.WorkingDirectory, merged.WorkingDirectory);
        Assert.True(merged.RunAsAdmin);
        Assert.Equal(source.CustomIconPath, merged.CustomIconPath);
        Assert.Equal(source.SortKey, merged.SortKey);
    }

    [Fact]
    public void The_source_is_left_alone()
    {
        var incoming = new AppData { UngroupedItems = { Item("Theirs") } };
        var sourceId = incoming.UngroupedItems[0].Id;

        WorkspaceMerge.Merge(new AppData(), incoming);

        Assert.Equal(sourceId, Assert.Single(incoming.UngroupedItems).Id);
    }

    [Fact]
    public void Two_items_pointing_at_the_same_file_are_both_kept()
    {
        // Same path, different arguments is a legitimate pair — collapsing them would destroy
        // data the user meant to have.
        var target = new AppData { UngroupedItems = { Item("Editor") } };
        var incoming = new AppData { UngroupedItems = { Item("Editor") } };

        WorkspaceMerge.Merge(target, incoming);

        Assert.Equal(2, target.UngroupedItems.Count);
    }

    // --- Folders ---

    [Fact]
    public void A_folder_the_destination_does_not_have_arrives_whole()
    {
        var incoming = new AppData { Folders = { Folder("Games", Item("Solitaire")) } };
        var target = new AppData();

        var result = WorkspaceMerge.Merge(target, incoming);

        var folder = Assert.Single(target.Folders);
        Assert.Equal("Games", folder.DisplayName);
        Assert.Equal("Solitaire", Assert.Single(folder.Children).DisplayName);
        Assert.Equal(1, result.FoldersCreated);
        Assert.Equal(0, result.FoldersMerged);
    }

    [Fact]
    public void A_folder_with_a_name_the_destination_already_has_is_merged_into_it()
    {
        var target = new AppData { Folders = { Folder("Games", Item("Mine")) } };
        var incoming = new AppData { Folders = { Folder("games ", Item("Theirs")) } };

        var result = WorkspaceMerge.Merge(target, incoming);

        var folder = Assert.Single(target.Folders);
        Assert.Equal("Games", folder.DisplayName);
        Assert.Equal(new[] { "Mine", "Theirs" }, Names(folder.Children));
        Assert.Equal(0, result.FoldersCreated);
        Assert.Equal(1, result.FoldersMerged);
    }

    [Fact]
    public void Two_incoming_folders_sharing_a_name_land_in_one_folder()
    {
        var incoming = new AppData { Folders = { Folder("Work", Item("A")), Folder("Work", Item("B")) } };
        var target = new AppData();

        var result = WorkspaceMerge.Merge(target, incoming);

        Assert.Equal(new[] { "A", "B" }, Names(Assert.Single(target.Folders).Children));
        Assert.Equal(1, result.FoldersCreated);
        Assert.Equal(1, result.FoldersMerged);
    }

    [Fact]
    public void A_merged_folder_keeps_its_own_id()
    {
        // The destination folder is the one the sidebar, the history stack and SelectedFolderId
        // already point at.
        var target = new AppData { Folders = { Folder("Games") } };
        var folderId = target.Folders[0].Id;
        var incoming = new AppData { Folders = { Folder("Games", Item("Theirs")) } };

        WorkspaceMerge.Merge(target, incoming);

        Assert.Equal(folderId, Assert.Single(target.Folders).Id);
    }

    [Fact]
    public void A_created_folder_does_not_reuse_the_incoming_id()
    {
        var incoming = new AppData { Folders = { Folder("Games") } };
        var target = new AppData();

        WorkspaceMerge.Merge(target, incoming);

        Assert.NotEqual(incoming.Folders[0].Id, Assert.Single(target.Folders).Id);
    }

    // --- Tags ---

    [Fact]
    public void A_tag_the_destination_already_has_by_name_is_reused_with_its_own_colour()
    {
        var mine = new TagItem { Name = "Work", ColorKey = "Blue" };
        var target = new AppData { Tags = { mine } };

        var theirs = new TagItem { Name = "work", ColorKey = "Red" };
        var incoming = new AppData { Tags = { theirs }, UngroupedItems = { Item("Theirs", theirs.Id) } };

        var result = WorkspaceMerge.Merge(target, incoming);

        var tag = Assert.Single(target.Tags);
        Assert.Equal("Blue", tag.ColorKey);
        Assert.Equal(mine.Id, Assert.Single(Assert.Single(target.UngroupedItems).TagIds));
        Assert.Equal(0, result.TagsCreated);
    }

    [Fact]
    public void A_new_tag_arrives_with_its_colour_but_a_fresh_id()
    {
        var theirs = new TagItem { Name = "Design", ColorKey = "Purple" };
        var incoming = new AppData { Tags = { theirs }, UngroupedItems = { Item("Theirs", theirs.Id) } };
        var target = new AppData();

        var result = WorkspaceMerge.Merge(target, incoming);

        var tag = Assert.Single(target.Tags);
        Assert.Equal("Purple", tag.ColorKey);
        Assert.NotEqual(theirs.Id, tag.Id);
        Assert.Equal(tag.Id, Assert.Single(Assert.Single(target.UngroupedItems).TagIds));
        Assert.Equal(1, result.TagsCreated);
    }

    [Fact]
    public void An_item_s_tags_come_out_in_destination_order()
    {
        // The invariant the tag dots on every tile rely on: two tiles carrying the same tags
        // show them in the same sequence.
        var work = new TagItem { Name = "Work" };
        var design = new TagItem { Name = "Design" };
        var target = new AppData { Tags = { work, design } };

        var theirDesign = new TagItem { Name = "Design" };
        var theirWork = new TagItem { Name = "Work" };
        var incoming = new AppData
        {
            Tags = { theirDesign, theirWork },
            UngroupedItems = { Item("Theirs", theirDesign.Id, theirWork.Id) }
        };

        WorkspaceMerge.Merge(target, incoming);

        Assert.Equal(new[] { work.Id, design.Id }, Assert.Single(target.UngroupedItems).TagIds);
    }

    [Fact]
    public void A_tag_id_the_import_never_defined_is_dropped()
    {
        var incoming = new AppData { UngroupedItems = { Item("Theirs", Guid.NewGuid()) } };
        var target = new AppData();

        WorkspaceMerge.Merge(target, incoming);

        Assert.Empty(Assert.Single(target.UngroupedItems).TagIds);
        Assert.Empty(target.Tags);
    }

    [Fact]
    public void Tags_on_foldered_items_are_mapped_too()
    {
        var theirs = new TagItem { Name = "Design" };
        var incoming = new AppData
        {
            Tags = { theirs },
            Folders = { Folder("Work", Item("Theirs", theirs.Id)) }
        };
        var target = new AppData();

        WorkspaceMerge.Merge(target, incoming);

        var merged = Assert.Single(Assert.Single(target.Folders).Children);
        Assert.Equal(Assert.Single(target.Tags).Id, Assert.Single(merged.TagIds));
    }

    // --- Recent launches ---

    [Fact]
    public void Imported_recents_follow_the_destination_s_own_and_point_at_the_new_ids()
    {
        var mine = Item("Mine");
        var target = new AppData
        {
            UngroupedItems = { mine },
            RecentLaunches = { new RecentLaunch { AppId = mine.Id, DisplayName = "Mine" } }
        };

        var theirs = Item("Theirs");
        var incoming = new AppData
        {
            UngroupedItems = { theirs },
            RecentLaunches = { new RecentLaunch { AppId = theirs.Id, DisplayName = "Theirs" } }
        };

        WorkspaceMerge.Merge(target, incoming);

        Assert.Equal(new[] { "Mine", "Theirs" }, target.RecentLaunches.Select(r => r.DisplayName));
        Assert.Equal(mine.Id, target.RecentLaunches[0].AppId);
        Assert.Equal(target.UngroupedItems[1].Id, target.RecentLaunches[1].AppId);
    }

    [Fact]
    public void A_recent_entry_whose_item_did_not_come_across_is_dropped()
    {
        var incoming = new AppData
        {
            RecentLaunches = { new RecentLaunch { AppId = Guid.NewGuid(), DisplayName = "Ghost" } }
        };

        var target = new AppData();
        WorkspaceMerge.Merge(target, incoming);

        // The list is keyed by item id and search reads the position as a rank, so a dangling
        // entry would rank nothing.
        Assert.Empty(target.RecentLaunches);
    }

    [Fact]
    public void Recents_are_trimmed_to_the_cap()
    {
        var target = new AppData();
        var incoming = new AppData();

        for (var i = 0; i < RecentLaunchList.MaxRecent + 5; i++)
        {
            var item = Item($"App{i}");
            incoming.UngroupedItems.Add(item);
            incoming.RecentLaunches.Add(new RecentLaunch { AppId = item.Id, DisplayName = item.DisplayName });
        }

        WorkspaceMerge.Merge(target, incoming);

        Assert.Equal(RecentLaunchList.MaxRecent, target.RecentLaunches.Count);
        Assert.Equal("App0", target.RecentLaunches[0].DisplayName);
    }

    // --- Degenerate input ---

    [Fact]
    public void Null_lists_from_a_hand_edited_file_do_not_throw()
    {
        // "Tags": null deserializes to a null list — property initializers only cover an
        // absent key, and an .acerun file is user-supplied.
        var target = new AppData { Tags = null!, UngroupedItems = null!, Folders = null!, RecentLaunches = null! };
        var incoming = new AppData { Tags = null!, UngroupedItems = null!, Folders = null!, RecentLaunches = null! };

        var result = WorkspaceMerge.Merge(target, incoming);

        Assert.Equal(0, result.ItemsAdded);
        Assert.NotNull(target.UngroupedItems);
        Assert.NotNull(target.Folders);
        Assert.NotNull(target.Tags);
        Assert.NotNull(target.RecentLaunches);
    }

    [Fact]
    public void Merging_an_empty_workspace_changes_nothing()
    {
        var target = new AppData { UngroupedItems = { Item("Mine") }, Folders = { Folder("Games") } };

        var result = WorkspaceMerge.Merge(target, new AppData());

        Assert.Equal(new MergeResult(0, 0, 0, 0), result);
        Assert.Single(target.UngroupedItems);
        Assert.Single(target.Folders);
    }
}
