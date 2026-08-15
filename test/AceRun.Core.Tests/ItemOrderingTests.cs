using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using ace_run.Services;
using Xunit;

namespace ace_run.Tests;

public class ItemOrderingTests
{
    private static readonly IReadOnlyList<Guid> NoTags = Array.Empty<Guid>();

    // --- By name ---

    [Fact]
    public void By_name_is_case_insensitive()
    {
        var ordered = Order(OrganizeBy.Name, new FakeItem("banana"), new FakeItem("Apple"));

        Assert.Equal(new[] { "Apple", "banana" }, Names(ordered));
    }

    // --- By path ---

    [Fact]
    public void By_path_compares_ordinally()
    {
        // Machine text, not display text — matching how Windows itself compares paths.
        var ordered = Order(OrganizeBy.Path,
            new FakeItem("B", @"C:\b.exe"),
            new FakeItem("A", @"C:\a.exe"));

        Assert.Equal(new[] { "A", "B" }, Names(ordered));
    }

    [Fact]
    public void By_path_falls_back_to_name_for_two_items_on_one_exe()
    {
        var ordered = Order(OrganizeBy.Path,
            new FakeItem("Zed", @"C:\chrome.exe"),
            new FakeItem("Alpha", @"C:\chrome.exe"));

        Assert.Equal(new[] { "Alpha", "Zed" }, Names(ordered));
    }

    // --- By sort key ---

    [Fact]
    public void Unkeyed_items_trail_the_keyed_ones()
    {
        // An item nobody has classified must not lead the folder just because an empty string
        // sorts first.
        var ordered = Order(OrganizeBy.SortKey,
            new FakeItem("NoKey"),
            new FakeItem("Keyed", sortKey: "b"),
            new FakeItem("AlsoKeyed", sortKey: "a"));

        Assert.Equal(new[] { "AlsoKeyed", "Keyed", "NoKey" }, Names(ordered));
    }

    [Fact]
    public void Items_sharing_a_sort_key_fall_back_to_name()
    {
        var ordered = Order(OrganizeBy.SortKey,
            new FakeItem("Zed", sortKey: "a"),
            new FakeItem("Alpha", sortKey: "a"));

        Assert.Equal(new[] { "Alpha", "Zed" }, Names(ordered));
    }

    // --- By tag ---

    [Fact]
    public void By_tag_ranks_on_workspace_tag_order_not_tag_name()
    {
        // "Work" is second in the workspace list even though it sorts first alphabetically.
        var design = new FakeTag("Design");
        var work = new FakeTag("Work");
        var tagOrder = new[] { design.Id, work.Id };

        var ordered = ItemOrdering.Order(
            new[]
            {
                new FakeItem("Tagged Work", "", "", "", work),
                new FakeItem("Tagged Design", "", "", "", design)
            },
            OrganizeBy.Tag,
            tagOrder);

        Assert.Equal(new[] { "Tagged Design", "Tagged Work" }, Names(ordered));
    }

    [Fact]
    public void Untagged_items_sort_last()
    {
        var work = new FakeTag("Work");

        var ordered = ItemOrdering.Order(
            new[] { new FakeItem("Bare"), new FakeItem("Tagged", "", "", "", work) },
            OrganizeBy.Tag,
            new[] { work.Id });

        Assert.Equal(new[] { "Tagged", "Bare" }, Names(ordered));
    }

    [Fact]
    public void An_item_whose_tag_the_workspace_no_longer_has_sorts_as_untagged()
    {
        var stale = new FakeTag("Deleted");
        var live = new FakeTag("Live");

        var ordered = ItemOrdering.Order(
            new[]
            {
                new FakeItem("Stale", "", "", "", stale),
                new FakeItem("Live", "", "", "", live)
            },
            OrganizeBy.Tag,
            new[] { live.Id });

        Assert.Equal(new[] { "Live", "Stale" }, Names(ordered));
    }

    [Fact]
    public void PrimaryTagRank_reads_only_the_first_tag()
    {
        var first = new FakeTag("First");
        var second = new FakeTag("Second");
        var item = new FakeItem("Both", "", "", "", second, first);

        // Tags arrive in workspace order, so the first one is the highest-ranked. Index 1
        // here, because `second` leads the item's own list.
        Assert.Equal(1, ItemOrdering.PrimaryTagRank(item, new[] { first.Id, second.Id }));
    }

    [Fact]
    public void PrimaryTagRank_of_an_untagged_item_is_MaxValue()
        => Assert.Equal(int.MaxValue, ItemOrdering.PrimaryTagRank(new FakeItem("Bare"), NoTags));

    // --- Stability ---

    [Fact]
    public void Items_equal_on_every_key_keep_the_order_the_user_dragged_them_into()
    {
        // Identical names: a stable sort preserves the incoming sequence, List.Sort would not.
        var a = new FakeItem("Same");
        var b = new FakeItem("Same");
        var c = new FakeItem("Same");

        var ordered = ItemOrdering.Order(new[] { c, a, b }, OrganizeBy.Name, NoTags);

        Assert.Same(c, ordered[0]);
        Assert.Same(a, ordered[1]);
        Assert.Same(b, ordered[2]);
    }

    // --- ApplyOrder ---

    [Fact]
    public void ApplyOrder_rearranges_in_place_and_reports_that_it_moved()
    {
        var a = new FakeItem("A");
        var b = new FakeItem("B");
        var collection = new ObservableCollection<FakeItem> { b, a };

        Assert.True(ItemOrdering.ApplyOrder(collection, new[] { a, b }));
        Assert.Equal(new[] { "A", "B" }, collection.Select(i => i.DisplayName));
    }

    [Fact]
    public void ApplyOrder_reports_no_move_when_already_in_order()
    {
        var a = new FakeItem("A");
        var b = new FakeItem("B");
        var collection = new ObservableCollection<FakeItem> { a, b };

        Assert.False(ItemOrdering.ApplyOrder(collection, new[] { a, b }));
    }

    [Fact]
    public void ApplyOrder_raises_only_Move_and_never_Reset()
    {
        // The invariant behind the whole method. A Reset recycles every GridView container,
        // which releases and reloads every icon — the grid visibly blinks. Clear + Add would
        // pass every other test here and fail this one.
        var items = Enumerable.Range(0, 5).Select(i => new FakeItem($"Item {i}")).ToList();
        var collection = new ObservableCollection<FakeItem>(items);

        var actions = new List<NotifyCollectionChangedAction>();
        collection.CollectionChanged += (_, e) => actions.Add(e.Action);

        ItemOrdering.ApplyOrder(collection, items.AsEnumerable().Reverse().ToList());

        Assert.NotEmpty(actions);
        Assert.All(actions, a => Assert.Equal(NotifyCollectionChangedAction.Move, a));
    }

    // --- MoveBy ---

    [Fact]
    public void MoveBy_shifts_the_item_and_reports_that_it_moved()
    {
        var a = new FakeItem("A");
        var b = new FakeItem("B");
        var c = new FakeItem("C");
        var collection = new ObservableCollection<FakeItem> { a, b, c };

        Assert.True(ItemOrdering.MoveBy(collection, c, -1));
        Assert.Equal(new[] { "A", "C", "B" }, collection.Select(i => i.DisplayName));

        Assert.True(ItemOrdering.MoveBy(collection, a, 1));
        Assert.Equal(new[] { "C", "A", "B" }, collection.Select(i => i.DisplayName));
    }

    [Fact]
    public void MoveBy_refuses_to_move_past_either_end()
    {
        var a = new FakeItem("A");
        var b = new FakeItem("B");
        var collection = new ObservableCollection<FakeItem> { a, b };

        Assert.False(ItemOrdering.MoveBy(collection, a, -1));
        Assert.False(ItemOrdering.MoveBy(collection, b, 1));
        Assert.Equal(new[] { "A", "B" }, collection.Select(i => i.DisplayName));
    }

    [Fact]
    public void MoveBy_reports_no_move_for_an_absent_item_or_a_zero_delta()
    {
        var a = new FakeItem("A");
        var collection = new ObservableCollection<FakeItem> { a };

        Assert.False(ItemOrdering.MoveBy(collection, new FakeItem("Stranger"), 1));
        Assert.False(ItemOrdering.MoveBy(collection, a, 0));
    }

    [Fact]
    public void MoveBy_raises_only_Move_and_never_Remove()
    {
        // The reason this exists rather than the caller doing RemoveAt + Insert: a Remove
        // recycles the row's container, so keyboard focus does not survive the move and the
        // menu item cannot be invoked twice in a row.
        var items = Enumerable.Range(0, 4).Select(i => new FakeItem($"Item {i}")).ToList();
        var collection = new ObservableCollection<FakeItem>(items);

        var actions = new List<NotifyCollectionChangedAction>();
        collection.CollectionChanged += (_, e) => actions.Add(e.Action);

        ItemOrdering.MoveBy(collection, items[3], -1);

        Assert.Equal(new[] { NotifyCollectionChangedAction.Move }, actions);
    }

    private static List<FakeItem> Order(OrganizeBy by, params FakeItem[] items)
        => ItemOrdering.Order(items, by, NoTags);

    private static IEnumerable<string> Names(IEnumerable<FakeItem> items)
        => items.Select(i => i.DisplayName);
}
