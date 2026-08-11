using System;
using System.Linq;
using ace_run.Models;
using ace_run.Services;
using Xunit;

namespace ace_run.Tests;

public class ItemFactoryTests
{
    [Fact]
    public void An_exe_is_named_after_its_file_and_rooted_in_its_folder()
    {
        var item = ItemFactory.FromPath(@"C:\Program Files\Tool\tool.exe");

        Assert.Equal(ItemKind.App, item.Kind);
        Assert.Equal("tool", item.DisplayName);
        Assert.Equal(@"C:\Program Files\Tool\tool.exe", item.FilePath);
        Assert.Equal(@"C:\Program Files\Tool", item.WorkingDirectory);
    }

    [Fact]
    public void A_url_item_gets_no_working_directory()
    {
        // GetDirectoryName on a URL yields junk like "https:\example.com".
        var item = ItemFactory.FromUrl("https://example.com/guide");

        Assert.Equal(ItemKind.Url, item.Kind);
        Assert.Equal("example.com", item.DisplayName);
        Assert.Equal("https://example.com/guide", item.FilePath);
        Assert.Equal(string.Empty, item.WorkingDirectory);
    }

    [Fact]
    public void An_empty_url_gets_an_empty_name()
    {
        // The "add a URL" dialog opens on this, with the user yet to type anything.
        var item = ItemFactory.FromUrl(string.Empty);

        Assert.Equal(ItemKind.Url, item.Kind);
        Assert.Equal(string.Empty, item.DisplayName);
    }

    [Fact]
    public void Each_new_item_gets_its_own_id()
        => Assert.NotEqual(ItemFactory.FromPath(@"C:\a.exe").Id, ItemFactory.FromPath(@"C:\a.exe").Id);
}

public class AppDataQueryTests
{
    private static AppData Sample() => new()
    {
        UngroupedItems = { new AppItem { DisplayName = "Loose" } },
        Folders =
        {
            new FolderItem
            {
                DisplayName = "Games",
                Children = { new AppItem { DisplayName = "A" }, new AppItem { DisplayName = "B" } }
            },
            new FolderItem { DisplayName = "Empty" }
        }
    };

    [Fact]
    public void AllItems_walks_ungrouped_first_then_each_folder()
    {
        var names = AppDataQuery.AllItems(Sample()).Select(i => i.DisplayName);

        Assert.Equal(new[] { "Loose", "A", "B" }, names);
    }

    [Fact]
    public void ItemIds_covers_foldered_items_too()
        => Assert.Equal(3, AppDataQuery.ItemIds(Sample()).Distinct().Count());

    [Fact]
    public void ItemCount_agrees_with_the_traversal()
    {
        var data = Sample();

        Assert.Equal(AppDataQuery.AllItems(data).Count(), data.ItemCount);
    }

    [Fact]
    public void An_empty_workspace_counts_zero()
        => Assert.Equal(0, new AppData().ItemCount);
}
