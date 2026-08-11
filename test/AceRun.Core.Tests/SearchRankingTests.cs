using System;
using System.Collections.Generic;
using System.Linq;
using ace_run.Models;
using ace_run.Services;
using Xunit;

namespace ace_run.Tests;

public class SearchRankingTests
{
    // --- RankOf ---

    [Fact]
    public void A_name_prefix_outranks_a_name_substring()
    {
        Assert.Equal(MatchRank.NamePrefix, SearchRanking.RankOf(new FakeItem("Notepad"), "note"));
        Assert.Equal(MatchRank.NameSubstring, SearchRanking.RankOf(new FakeItem("GNU Notepad"), "note"));
    }

    [Fact]
    public void Matching_is_case_insensitive()
        => Assert.Equal(MatchRank.NamePrefix, SearchRanking.RankOf(new FakeItem("NOTEPAD"), "note"));

    [Fact]
    public void A_tag_name_doubles_as_a_query()
    {
        var item = new FakeItem("Blender", tags: new FakeTag("Design"));

        Assert.Equal(MatchRank.Tag, SearchRanking.RankOf(item, "design"));
    }

    [Fact]
    public void Tags_are_matched_one_by_one_not_as_a_joined_string()
    {
        // If tags were joined with a separator before matching, a query spanning the boundary
        // would hit. "rkdes" spans "Work" + "Design"; it must not match.
        var item = new FakeItem("Thing", "", "", "", new FakeTag("Work"), new FakeTag("Design"));

        Assert.Equal(MatchRank.None, SearchRanking.RankOf(item, "rkdes"));
        Assert.Equal(MatchRank.Tag, SearchRanking.RankOf(item, "Work"));
    }

    [Fact]
    public void Path_is_matched_so_a_url_is_findable_by_its_domain()
    {
        var item = new FakeItem("Docs", filePath: "https://example.com/guide");

        Assert.Equal(MatchRank.Path, SearchRanking.RankOf(item, "example.com"));
    }

    [Fact]
    public void Arguments_are_matched_so_two_entries_on_one_exe_can_be_told_apart()
    {
        var item = new FakeItem("Chrome", filePath: @"C:\chrome.exe", arguments: "--profile-directory=Work");

        Assert.Equal(MatchRank.Path, SearchRanking.RankOf(item, "profile-directory"));
    }

    [Fact]
    public void Path_and_arguments_share_one_rank()
    {
        var byPath = new FakeItem("A", filePath: @"C:\tools\zed.exe");
        var byArgs = new FakeItem("B", arguments: "--zed");

        Assert.Equal(SearchRanking.RankOf(byPath, "zed"), SearchRanking.RankOf(byArgs, "zed"));
    }

    [Fact]
    public void Nothing_matching_ranks_None()
        => Assert.Equal(MatchRank.None, SearchRanking.RankOf(new FakeItem("Notepad"), "gimp"));

    [Fact]
    public void Rank_order_is_name_then_tag_then_path()
    {
        // The enum's order is the sort key, so it is part of the contract.
        Assert.True(MatchRank.NamePrefix < MatchRank.NameSubstring);
        Assert.True(MatchRank.NameSubstring < MatchRank.Tag);
        Assert.True(MatchRank.Tag < MatchRank.Path);
        Assert.True(MatchRank.Path < MatchRank.None);
    }

    // --- Rank ---

    [Fact]
    public void Non_matching_items_are_filtered_out()
    {
        var results = Rank("note", new FakeItem("Notepad"), new FakeItem("Gimp"));

        Assert.Equal("Notepad", Assert.Single(results).Item.DisplayName);
    }

    [Fact]
    public void Better_matches_come_first()
    {
        var results = Rank("note",
            new FakeItem("A", filePath: @"C:\notes\a.exe"),   // Path
            new FakeItem("My Notepad"),                        // NameSubstring
            new FakeItem("Notepad"));                          // NamePrefix

        Assert.Equal(new[] { "Notepad", "My Notepad", "A" }, results.Select(r => r.Item.DisplayName));
    }

    [Fact]
    public void Recency_beats_match_quality_outright()
    {
        var best = new FakeItem("Notepad");            // NamePrefix
        var worst = new FakeItem("A", filePath: @"C:\note.exe"); // Path

        // The weaker match was launched recently, so it leads regardless.
        var results = Rank("note", new[] { best, worst }, recents: new[] { worst });

        Assert.Equal(new[] { "A", "Notepad" }, results.Select(r => r.Item.DisplayName));
    }

    [Fact]
    public void Within_the_recent_bucket_launch_order_decides()
    {
        var first = new FakeItem("Note one");
        var second = new FakeItem("Note two");

        // recents is most-recent-first, so `second` leads even though it comes later in the
        // collection.
        var results = Rank("note", new[] { first, second }, recents: new[] { second, first });

        Assert.Equal(new[] { "Note two", "Note one" }, results.Select(r => r.Item.DisplayName));
    }

    [Fact]
    public void Equally_good_hits_keep_the_order_they_were_enumerated_in()
    {
        // The user's dragged arrangement is the last tiebreak. A stable sort is what preserves
        // it; List.Sort would shuffle these.
        var results = Rank("note",
            new FakeItem("Note C"), new FakeItem("Note A"), new FakeItem("Note B"));

        Assert.Equal(new[] { "Note C", "Note A", "Note B" }, results.Select(r => r.Item.DisplayName));
    }

    [Fact]
    public void Folder_labels_travel_with_their_items()
    {
        var results = SearchRanking.Rank("note", new[]
        {
            new SearchCandidate<FakeItem>(new FakeItem("Notepad"), "Ungrouped"),
            new SearchCandidate<FakeItem>(new FakeItem("Notes"), "Work")
        });

        Assert.Equal(new[] { "Ungrouped", "Work" }, results.Select(r => r.FolderLabel));
    }

    [Fact]
    public void A_recent_entry_for_an_item_that_did_not_match_is_ignored()
    {
        var match = new FakeItem("Notepad");
        var gone = new FakeItem("Gimp");

        var results = Rank("note", new[] { match }, recents: new[] { gone });

        Assert.Equal("Notepad", Assert.Single(results).Item.DisplayName);
    }

    [Fact]
    public void No_recents_is_the_same_as_an_empty_history()
    {
        var items = new[] { new FakeItem("Notepad"), new FakeItem("My Note") };

        var withNull = SearchRanking.Rank("note", Candidates(items), null);
        var withEmpty = SearchRanking.Rank("note", Candidates(items), Array.Empty<RecentLaunch>());

        Assert.Equal(withNull.Select(r => r.Item.DisplayName), withEmpty.Select(r => r.Item.DisplayName));
    }

    private static IEnumerable<SearchCandidate<FakeItem>> Candidates(IEnumerable<FakeItem> items)
        => items.Select(i => new SearchCandidate<FakeItem>(i, "Ungrouped"));

    private static List<SearchCandidate<FakeItem>> Rank(string query, params FakeItem[] items)
        => SearchRanking.Rank(query, Candidates(items));

    private static List<SearchCandidate<FakeItem>> Rank(
        string query, FakeItem[] items, FakeItem[] recents)
        => SearchRanking.Rank(
            query,
            Candidates(items),
            recents.Select(r => new RecentLaunch { AppId = r.Id, DisplayName = r.DisplayName }).ToList());
}
