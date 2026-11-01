using System;
using System.Collections.Generic;
using System.Linq;
using ace_run.Automation;
using ace_run.Models;
using Xunit;

namespace ace_run.Tests;

/// <summary>
/// The projection the automation interface answers with, and the search it borrows from the
/// window. Search parity is the point of several of these: an agent and the search box must
/// agree on which items hit and in what order.
/// </summary>
public sealed class AutomationQueryTests
{
    private const string Ws = "Work";

    // --- Listing ---

    [Fact]
    public void Items_lists_the_whole_workspace_ungrouped_first_each_naming_its_folder()
    {
        var data = new AppData();
        data.UngroupedItems.Add(App("Notepad"));
        var tools = new FolderItem { DisplayName = "Tools" };
        tools.Children.Add(App("Hex Editor"));
        data.Folders.Add(tools);

        var items = AutomationQuery.Items(data, Ws);

        Assert.Equal(new[] { "Notepad", "Hex Editor" }, items.Select(i => i.Name));
        Assert.Null(items[0].Folder);
        Assert.Equal("Tools", items[1].Folder);
        Assert.All(items, i => Assert.Equal(Ws, i.Workspace));
    }

    [Fact]
    public void Items_for_one_folder_lists_only_its_children()
    {
        var data = new AppData();
        data.UngroupedItems.Add(App("Notepad"));
        var tools = new FolderItem { DisplayName = "Tools" };
        tools.Children.Add(App("Hex Editor"));
        data.Folders.Add(tools);

        var items = AutomationQuery.Items(data, Ws, tools);

        Assert.Equal("Hex Editor", Assert.Single(items).Name);
    }

    [Fact]
    public void Folders_tags_and_recents_project_their_ids_through()
    {
        var data = new AppData();
        var tag = new TagItem { Name = "Work", ColorKey = "Green" };
        data.Tags.Add(tag);
        var folder = new FolderItem { DisplayName = "Tools" };
        folder.Children.Add(App("Hex Editor"));
        data.Folders.Add(folder);
        data.RecentLaunches.Add(new RecentLaunch { AppId = folder.Children[0].Id, DisplayName = "Hex Editor" });

        Assert.Equal(folder.Id, Assert.Single(AutomationQuery.Folders(data, Ws)).Id);
        Assert.Equal(1, Assert.Single(AutomationQuery.Folders(data, Ws)).ItemCount);
        Assert.Equal("Green", Assert.Single(AutomationQuery.Tags(data, Ws)).Color);
        Assert.Equal(tag.Id, Assert.Single(AutomationQuery.Tags(data, Ws)).Id);
        Assert.Equal(folder.Children[0].Id, Assert.Single(AutomationQuery.Recents(data, Ws)).Id);
        Assert.Equal(Ws, Assert.Single(AutomationQuery.Tags(data, Ws)).Workspace);
    }

    [Fact]
    public void Workspaces_marks_the_active_one_and_reports_the_stored_count()
    {
        var config = new WorkspaceConfig();
        var work = new WorkspaceInfo { Name = "Work", ColorTag = "Blue", AppCount = 7 };
        var games = new WorkspaceInfo { Name = "Games" };
        config.Workspaces.Add(work);
        config.Workspaces.Add(games);
        config.ActiveWorkspaceId = games.Id;

        var summaries = AutomationQuery.Workspaces(config);

        Assert.False(summaries[0].IsActive);
        Assert.True(summaries[1].IsActive);
        Assert.Equal("Blue", summaries[0].Color);
        Assert.Equal(7, summaries[0].ItemCount);
        Assert.Null(summaries[1].Color);
    }

    // --- Filtering by tag ---

    [Fact]
    public void Items_filtered_by_tag_returns_only_the_items_carrying_it()
    {
        var data = new AppData();
        var done = new TagItem { Name = "Done" };
        var other = new TagItem { Name = "Progress" };
        data.Tags.Add(done);
        data.Tags.Add(other);

        var tagged = App("Finished");
        tagged.TagIds.Add(done.Id);
        var untagged = App("Plain");
        var elsewhere = App("Busy");
        elsewhere.TagIds.Add(other.Id);
        data.UngroupedItems.Add(tagged);
        data.UngroupedItems.Add(untagged);
        data.UngroupedItems.Add(elsewhere);

        var inFolder = App("Finished too");
        inFolder.TagIds.Add(done.Id);
        var folder = new FolderItem { DisplayName = "Tools" };
        folder.Children.Add(inFolder);
        folder.Children.Add(App("Also plain"));
        data.Folders.Add(folder);

        var items = AutomationQuery.Items(data, Ws, folder: null, tagId: done.Id);

        Assert.Equal(new[] { "Finished", "Finished too" }, items.Select(i => i.Name));
    }

    [Fact]
    public void A_tag_filter_applies_inside_a_folder_too()
    {
        var data = new AppData();
        var done = new TagItem { Name = "Done" };
        data.Tags.Add(done);

        var folder = new FolderItem { DisplayName = "Tools" };
        var hit = App("Finished");
        hit.TagIds.Add(done.Id);
        folder.Children.Add(hit);
        folder.Children.Add(App("Plain"));
        data.Folders.Add(folder);

        Assert.Equal("Finished", Assert.Single(AutomationQuery.Items(data, Ws, folder, done.Id)).Name);
    }

    [Fact]
    public void An_unknown_tag_id_matches_nothing_rather_than_everything()
    {
        var data = new AppData();
        data.UngroupedItems.Add(App("Plain"));

        Assert.Empty(AutomationQuery.Items(data, Ws, null, Guid.NewGuid()));
    }

    // --- Paging ---

    [Fact]
    public void A_page_reports_the_total_beside_the_slice()
    {
        var data = new AppData();
        for (var i = 0; i < 10; i++) data.UngroupedItems.Add(App($"App {i}"));

        var page = AutomationQuery.Page(AutomationQuery.Items(data, Ws), offset: 4, limit: 3);

        Assert.Equal(10, page.Total);
        Assert.Equal(4, page.Offset);
        Assert.Equal(new[] { "App 4", "App 5", "App 6" }, page.Items.Select(i => i.Name));
        Assert.Equal(7, page.NextOffset);
    }

    [Fact]
    public void Walking_the_pages_by_NextOffset_visits_every_item_exactly_once()
    {
        // The reason NextOffset exists: an agent computing the next offset itself overlapped
        // two pages and reported counts one too high. Following this number cannot do that.
        var data = new AppData();
        for (var i = 0; i < 110; i++) data.UngroupedItems.Add(App($"App {i}"));
        var items = AutomationQuery.Items(data, Ws);

        var seen = new List<string>();
        int? offset = 0;

        while (offset is int at)
        {
            var page = AutomationQuery.Page(items, at, limit: 50);
            seen.AddRange(page.Items.Select(i => i.Name));
            offset = page.NextOffset;
        }

        Assert.Equal(110, seen.Count);
        Assert.Equal(110, seen.Distinct().Count());
        Assert.Equal(items.Select(i => i.Name), seen);
    }

    [Fact]
    public void The_last_page_has_no_NextOffset()
    {
        var data = new AppData();
        for (var i = 0; i < 10; i++) data.UngroupedItems.Add(App($"App {i}"));
        var items = AutomationQuery.Items(data, Ws);

        Assert.Null(AutomationQuery.Page(items, offset: 5, limit: 5).NextOffset);
        Assert.Null(AutomationQuery.Page(items).NextOffset);
        Assert.Null(AutomationQuery.Page(items, offset: 500).NextOffset);
    }

    [Fact]
    public void A_page_defaults_to_the_documented_limit_and_cannot_exceed_the_maximum()
    {
        var data = new AppData();
        for (var i = 0; i < AutomationQuery.MaxLimit + 50; i++) data.UngroupedItems.Add(App($"App {i}"));
        var items = AutomationQuery.Items(data, Ws);

        Assert.Equal(AutomationQuery.DefaultLimit, AutomationQuery.Page(items).Items.Count);
        Assert.Equal(AutomationQuery.MaxLimit, AutomationQuery.Page(items, limit: 10_000).Items.Count);
    }

    [Fact]
    public void An_offset_past_the_end_is_an_empty_page_not_an_error()
    {
        var data = new AppData();
        data.UngroupedItems.Add(App("Only one"));
        var page = AutomationQuery.Page(AutomationQuery.Items(data, Ws), offset: 500);

        Assert.Equal(1, page.Total);
        Assert.Empty(page.Items);
    }

    // --- Per-item projection ---

    [Fact]
    public void An_app_item_reports_its_launch_fields()
    {
        var data = new AppData();
        data.UngroupedItems.Add(new AppItem
        {
            DisplayName = "Game",
            FilePath = @"C:\Games\game.exe",
            Arguments = "-windowed",
            WorkingDirectory = @"D:\Saves",
            RunAsAdmin = true
        });

        var item = Assert.Single(AutomationQuery.Items(data, Ws));

        Assert.Equal("App", item.Kind);
        Assert.Equal(@"C:\Games\game.exe", item.Target);
        Assert.Equal("-windowed", item.Arguments);
        Assert.Equal(@"D:\Saves", item.WorkingDirectory);
        Assert.True(item.RunAsAdmin);
    }

    [Fact]
    public void A_working_directory_that_is_just_the_targets_own_folder_is_omitted()
    {
        // What ItemFactory.FromPath sets, so it is what nearly every item has — and on a
        // listing of hundreds it is a near-copy of Target on every row.
        var data = new AppData();
        data.UngroupedItems.Add(new AppItem
        {
            DisplayName = "Game",
            FilePath = @"C:\Games\game.exe",
            WorkingDirectory = @"C:\Games"
        });

        Assert.Null(Assert.Single(AutomationQuery.Items(data, Ws)).WorkingDirectory);
    }

    [Fact]
    public void A_trailing_separator_does_not_make_the_working_directory_look_chosen()
    {
        var data = new AppData();
        data.UngroupedItems.Add(new AppItem
        {
            DisplayName = "Game",
            FilePath = @"C:\Games\game.exe",
            WorkingDirectory = @"C:\Games\"
        });

        Assert.Null(Assert.Single(AutomationQuery.Items(data, Ws)).WorkingDirectory);
    }

    [Fact]
    public void A_path_no_filesystem_would_accept_still_lists()
    {
        // Pins that the projection does no path validation: these strings come from whatever
        // the user typed or dropped, and one bad row must not fail the whole listing.
        var data = new AppData();
        data.UngroupedItems.Add(new AppItem
        {
            DisplayName = "Odd",
            FilePath = "C:\\we|rd\u0001\\thing.exe",
            WorkingDirectory = "nowhere in particular"
        });

        var item = Assert.Single(AutomationQuery.Items(data, Ws));

        Assert.Equal("nowhere in particular", item.WorkingDirectory);
    }

    [Fact]
    public void Unset_text_fields_are_reported_as_absent_not_as_empty_strings()
    {
        var data = new AppData();
        data.UngroupedItems.Add(App("Notepad"));

        var item = Assert.Single(AutomationQuery.Items(data, Ws));

        Assert.Null(item.Arguments);
        Assert.Null(item.WorkingDirectory);
        Assert.Null(item.Tags);
    }

    [Fact]
    public void A_url_item_hides_the_exe_only_fields_whatever_the_file_says()
    {
        // 7-url-items.md §5: a URL launch passes neither arguments nor working directory and
        // never uses runas. Reporting stored leftovers would describe a launch that cannot
        // happen.
        var data = new AppData();
        data.UngroupedItems.Add(new AppItem
        {
            Kind = ItemKind.Url,
            DisplayName = "Docs",
            FilePath = "https://example.com/docs",
            Arguments = "-stale",
            WorkingDirectory = @"C:\stale",
            RunAsAdmin = true
        });

        var item = Assert.Single(AutomationQuery.Items(data, Ws));

        Assert.Equal("Url", item.Kind);
        Assert.Equal("https://example.com/docs", item.Target);
        Assert.Null(item.Arguments);
        Assert.Null(item.WorkingDirectory);
        Assert.False(item.RunAsAdmin);
    }

    [Fact]
    public void Tags_come_back_in_workspace_order_with_dead_ids_dropped()
    {
        var data = new AppData();
        var work = new TagItem { Name = "Work" };
        var fun = new TagItem { Name = "Fun" };
        data.Tags.Add(work);
        data.Tags.Add(fun);

        var item = App("Thing");
        // Assigned out of order and carrying a tag the workspace no longer has.
        item.TagIds.Add(fun.Id);
        item.TagIds.Add(Guid.NewGuid());
        item.TagIds.Add(work.Id);
        data.UngroupedItems.Add(item);

        Assert.Equal(new[] { "Work", "Fun" }, Assert.Single(AutomationQuery.Items(data, Ws)).Tags!);
    }

    // --- Search ---

    [Fact]
    public void Search_puts_a_name_prefix_above_a_name_substring()
    {
        var data = new AppData();
        data.UngroupedItems.Add(App("My Notepad"));
        data.UngroupedItems.Add(App("Notepad"));

        Assert.Equal(
            new[] { "Notepad", "My Notepad" },
            AutomationQuery.Search(data, Ws, "note").Select(i => i.Name));
    }

    [Fact]
    public void Search_puts_a_recent_launch_on_top_however_well_the_others_match()
    {
        var data = new AppData();
        data.UngroupedItems.Add(App("Notepad"));
        var other = App("Note Taker");
        data.UngroupedItems.Add(other);
        data.RecentLaunches.Add(new RecentLaunch { AppId = other.Id, DisplayName = other.DisplayName });

        Assert.Equal(
            new[] { "Note Taker", "Notepad" },
            AutomationQuery.Search(data, Ws, "note").Select(i => i.Name));
    }

    [Fact]
    public void Search_matches_path_and_tag_as_well_as_name()
    {
        var data = new AppData();
        var tag = new TagItem { Name = "Gaming" };
        data.Tags.Add(tag);

        var byPath = App("Thing", @"C:\Steam\thing.exe");
        var byTag = App("Other");
        byTag.TagIds.Add(tag.Id);
        data.UngroupedItems.Add(byPath);
        data.UngroupedItems.Add(byTag);

        Assert.Equal("Thing", Assert.Single(AutomationQuery.Search(data, Ws, "steam")).Name);
        Assert.Equal("Other", Assert.Single(AutomationQuery.Search(data, Ws, "gam")).Name);
    }

    [Fact]
    public void Search_finds_items_inside_folders_and_says_where_they_are()
    {
        var data = new AppData();
        var tools = new FolderItem { DisplayName = "Tools" };
        tools.Children.Add(App("Hex Editor"));
        data.Folders.Add(tools);

        var hit = Assert.Single(AutomationQuery.Search(data, Ws, "hex"));

        Assert.Equal("Tools", hit.Folder);
    }

    [Fact]
    public void An_empty_query_is_rejected_rather_than_treated_as_everything()
    {
        // SearchRanking matches every item on an empty query; listing is a different question
        // with its own tool, so answering it here would be a quiet lie about what was asked.
        var data = new AppData();
        data.UngroupedItems.Add(App("Notepad"));

        Assert.Throws<ArgumentException>(() => AutomationQuery.Search(data, Ws, "  "));
    }

    [Fact]
    public void FolderOf_names_where_an_item_sits_and_null_when_it_sits_nowhere()
    {
        // What the write side uses after resolving an item by name, to report where it ended
        // up. The read side walks the folders anyway and never needs it.
        var data = new AppData();
        var loose = App("Notepad");
        data.UngroupedItems.Add(loose);
        var tools = new FolderItem { DisplayName = "Tools" };
        var hex = App("Hex Editor");
        tools.Children.Add(hex);
        data.Folders.Add(tools);

        Assert.Same(tools, AutomationQuery.FolderOf(data, hex.Id));
        Assert.Null(AutomationQuery.FolderOf(data, loose.Id));
        Assert.Null(AutomationQuery.FolderOf(data, Guid.NewGuid()));
    }

    private static AppItem App(string name, string path = "") =>
        new() { DisplayName = name, FilePath = path };
}
