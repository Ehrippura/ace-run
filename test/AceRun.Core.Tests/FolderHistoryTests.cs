using System;
using System.Collections.Generic;
using System.Linq;
using ace_run.Services;
using Xunit;

namespace ace_run.Tests;

public class FolderHistoryTests
{
    private readonly Guid _a = Guid.NewGuid();
    private readonly Guid _b = Guid.NewGuid();
    private readonly Guid _c = Guid.NewGuid();

    /// <summary>A workspace containing exactly these folders.</summary>
    private static Func<Guid, bool> Live(params Guid[] ids) => id => ids.Contains(id);

    // --- The invariant: if Back is enabled, a press moves ---

    [Fact]
    public void A_fresh_history_cannot_go_back()
        => Assert.False(new FolderHistory().CanGoBack);

    [Fact]
    public void Recording_a_move_enables_back()
    {
        var history = new FolderHistory();
        history.Record(null); // left ungrouped

        Assert.True(history.CanGoBack);
    }

    [Fact]
    public void Back_returns_where_the_user_came_from()
    {
        var history = new FolderHistory();
        history.Record(null);   // ungrouped -> A

        Assert.True(history.TryGoBack(_a, Live(_a), out var target));
        Assert.Null(target);    // back to ungrouped
    }

    [Fact]
    public void Back_walks_the_stack_in_reverse()
    {
        var history = new FolderHistory();
        history.Record(null);   // ungrouped -> A
        history.Record(_a);     // A -> B

        Assert.True(history.TryGoBack(_b, Live(_a, _b), out var first));
        Assert.Equal(_a, first);

        Assert.True(history.TryGoBack(_a, Live(_a, _b), out var second));
        Assert.Null(second);

        Assert.False(history.CanGoBack);
    }

    // --- Dead entries ---

    [Fact]
    public void An_entry_for_a_deleted_folder_is_not_navigable()
        => Assert.False(FolderHistory.IsNavigable(_a, null, Live(_b)));

    [Fact]
    public void An_entry_for_where_the_user_already_stands_is_not_navigable()
        => Assert.False(FolderHistory.IsNavigable(_a, _a, Live(_a)));

    [Fact]
    public void Ungrouped_is_navigable_only_from_inside_a_folder()
    {
        Assert.True(FolderHistory.IsNavigable(null, _a, Live(_a)));
        Assert.False(FolderHistory.IsNavigable(null, null, Live(_a)));
    }

    [Fact]
    public void Trim_drops_a_dead_entry_from_the_top()
    {
        var history = new FolderHistory();
        history.Record(_a);     // A is about to be deleted

        history.Trim(null, Live(_b));

        Assert.False(history.CanGoBack);
    }

    [Fact]
    public void Trim_drops_an_entry_pointing_at_where_the_user_now_stands()
    {
        // Evicted from a deleted folder back to ungrouped, when ungrouped was already what
        // Back pointed at — the second of the two ways a dead entry reaches the top.
        var history = new FolderHistory();
        history.Record(null);

        history.Trim(null, Live(_a));

        Assert.False(history.CanGoBack);
    }

    [Fact]
    public void Trim_stops_at_the_first_live_entry()
    {
        var history = new FolderHistory();
        history.Record(_a);     // deeper, still live
        history.Record(_b);     // top, about to die

        history.Trim(null, Live(_a));

        Assert.True(history.CanGoBack);
        Assert.True(history.TryGoBack(null, Live(_a), out var target));
        Assert.Equal(_a, target);
    }

    [Fact]
    public void TryGoBack_skips_dead_entries_even_without_a_trim()
    {
        // Trim normally clears the top, so this is the backstop path.
        var history = new FolderHistory();
        history.Record(_a);     // live
        history.Record(_b);     // dead
        history.Record(_c);     // dead

        Assert.True(history.TryGoBack(null, Live(_a), out var target));
        Assert.Equal(_a, target);
    }

    [Fact]
    public void TryGoBack_with_nothing_navigable_empties_the_stack_and_reports_false()
    {
        var history = new FolderHistory();
        history.Record(_b);
        history.Record(_c);

        Assert.False(history.TryGoBack(null, Live(_a), out var target));
        Assert.Null(target);
        Assert.False(history.CanGoBack);
    }

    // --- Prune ---

    [Fact]
    public void Prune_removes_a_deleted_folder_from_every_depth()
    {
        // Trim only guarantees the top; an entry left deeper down would make a later press
        // land nowhere. Interleaved so a top-only sweep would leave one behind.
        var history = new FolderHistory();
        history.Record(_b);
        history.Record(_a);
        history.Record(_b);
        history.Record(_c);

        history.Prune(_b, null, Live(_a, _c));

        Assert.Equal(2, history.Count);

        // Only the surviving folders are reachable, in reverse order, and _b never appears.
        var visited = new List<Guid?>();
        Guid? current = null;
        while (history.TryGoBack(current, Live(_a, _c), out var target))
        {
            visited.Add(target);
            current = target;
        }

        Assert.Equal(new Guid?[] { _c, _a }, visited);
    }

    [Fact]
    public void Going_back_to_where_you_already_stand_is_not_a_move()
    {
        // The invariant, stated directly: a duplicate entry must not spend a press doing
        // nothing visible.
        var history = new FolderHistory();
        history.Record(_a);
        history.Record(_a);

        Assert.True(history.TryGoBack(null, Live(_a), out var first));
        Assert.Equal(_a, first);

        // Now standing at _a, the remaining _a entry is dead.
        Assert.False(history.TryGoBack(_a, Live(_a), out _));
    }

    [Fact]
    public void Prune_also_trims_what_it_uncovers()
    {
        var history = new FolderHistory();
        history.Record(null);   // would leave the user where they already are
        history.Record(_b);     // deleted

        history.Prune(_b, null, Live(_a));

        Assert.False(history.CanGoBack);
    }

    // --- Bounds and lifetime ---

    [Fact]
    public void The_stack_is_capped_and_drops_the_oldest_entry()
    {
        var history = new FolderHistory();
        var ids = Enumerable.Range(0, FolderHistory.MaxDepth + 1)
                            .Select(_ => Guid.NewGuid())
                            .ToList();

        foreach (var id in ids)
            history.Record(id);

        Assert.Equal(FolderHistory.MaxDepth, history.Count);

        // The very first id was evicted; the second is now the deepest entry.
        var live = Live(ids.ToArray());
        Guid? deepest = null;
        while (history.TryGoBack(null, live, out var target))
            deepest = target;

        Assert.Equal(ids[1], deepest);
    }

    [Fact]
    public void Clear_empties_the_stack()
    {
        var history = new FolderHistory();
        history.Record(_a);
        history.Record(_b);

        history.Clear();

        Assert.False(history.CanGoBack);
        Assert.Equal(0, history.Count);
    }
}
