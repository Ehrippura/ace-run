using System;
using System.Collections.Generic;
using System.Linq;
using ace_run.Models;
using ace_run.Services;
using Xunit;

namespace ace_run.Tests;

public class RecentLaunchListTests
{
    [Fact]
    public void A_launch_goes_to_the_front()
    {
        var recents = new List<RecentLaunch>();
        var first = new FakeItem("First");
        var second = new FakeItem("Second");

        RecentLaunchList.Track(recents, first);
        RecentLaunchList.Track(recents, second);

        Assert.Equal(new[] { "Second", "First" }, recents.Select(r => r.DisplayName));
    }

    [Fact]
    public void Relaunching_moves_an_item_up_rather_than_duplicating_it()
    {
        // Search reads the index as a rank, which only works if an id appears once.
        var recents = new List<RecentLaunch>();
        var a = new FakeItem("A");
        var b = new FakeItem("B");

        RecentLaunchList.Track(recents, a);
        RecentLaunchList.Track(recents, b);
        RecentLaunchList.Track(recents, a);

        Assert.Equal(new[] { "A", "B" }, recents.Select(r => r.DisplayName));
        Assert.Single(recents, r => r.AppId == a.Id);
    }

    [Fact]
    public void The_launched_item_carries_its_name_and_path()
    {
        var recents = new List<RecentLaunch>();
        var item = new FakeItem("Notepad", @"C:\notepad.exe");

        RecentLaunchList.Track(recents, item);

        var entry = Assert.Single(recents);
        Assert.Equal(item.Id, entry.AppId);
        Assert.Equal("Notepad", entry.DisplayName);
        Assert.Equal(@"C:\notepad.exe", entry.FilePath);
    }

    [Fact]
    public void The_list_is_capped_and_the_oldest_entry_falls_off()
    {
        var recents = new List<RecentLaunch>();
        var items = Enumerable.Range(0, RecentLaunchList.MaxRecent + 3)
                              .Select(i => new FakeItem($"Item {i}"))
                              .ToList();

        foreach (var item in items)
            RecentLaunchList.Track(recents, item);

        Assert.Equal(RecentLaunchList.MaxRecent, recents.Count);
        Assert.Equal(items[^1].DisplayName, recents[0].DisplayName);
        Assert.DoesNotContain(recents, r => r.AppId == items[0].Id);
    }

    [Fact]
    public void Purge_drops_entries_whose_item_is_gone()
    {
        var live = new FakeItem("Live");
        var deleted = new FakeItem("Deleted");
        var recents = new List<RecentLaunch>();
        RecentLaunchList.Track(recents, live);
        RecentLaunchList.Track(recents, deleted);

        Assert.True(RecentLaunchList.Purge(recents, new HashSet<Guid> { live.Id }));
        Assert.Equal("Live", Assert.Single(recents).DisplayName);
    }

    [Fact]
    public void Purge_reports_no_change_when_everything_is_still_there()
    {
        var item = new FakeItem("Live");
        var recents = new List<RecentLaunch>();
        RecentLaunchList.Track(recents, item);

        Assert.False(RecentLaunchList.Purge(recents, new HashSet<Guid> { item.Id }));
        Assert.Single(recents);
    }

    [Fact]
    public void Purge_on_an_empty_list_reports_no_change()
        => Assert.False(RecentLaunchList.Purge(new List<RecentLaunch>(), new HashSet<Guid>()));
}
