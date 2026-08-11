using System;
using System.Collections.Generic;
using System.Linq;
using ace_run.Services;
using Xunit;

namespace ace_run.Tests;

public class TagOrderingTests
{
    private readonly FakeTag _work = new("Work");
    private readonly FakeTag _design = new("Design");
    private readonly FakeTag _games = new("Games");

    private List<FakeTag> Workspace => new() { _work, _design, _games };

    // --- InWorkspaceOrder ---

    [Fact]
    public void Ids_come_back_in_workspace_order_not_the_order_given()
    {
        // The fixed order is what lets the tag dots on two tiles line up.
        var result = TagOrdering.InWorkspaceOrder(Workspace, new HashSet<Guid> { _games.Id, _work.Id });

        Assert.Equal(new[] { "Work", "Games" }, result.Select(t => t.Name));
    }

    [Fact]
    public void Ids_the_workspace_no_longer_has_are_dropped()
    {
        var deleted = new FakeTag("Deleted");

        var result = TagOrdering.InWorkspaceOrder(Workspace, new HashSet<Guid> { _work.Id, deleted.Id });

        Assert.Equal("Work", Assert.Single(result).Name);
    }

    [Fact]
    public void An_empty_id_set_yields_nothing()
        => Assert.Empty(TagOrdering.InWorkspaceOrder(Workspace, new HashSet<Guid>()));

    // --- WithTagToggled ---

    [Fact]
    public void Assigning_a_tag_inserts_it_in_workspace_position()
    {
        // Design is second in the workspace, so it lands before Games rather than at the end.
        var current = new List<FakeTag> { _work, _games };

        var result = TagOrdering.WithTagToggled(Workspace, current, _design.Id, assign: true);

        Assert.NotNull(result);
        Assert.Equal(new[] { "Work", "Design", "Games" }, result.Select(t => t.Name));
    }

    [Fact]
    public void Removing_a_tag_leaves_the_rest_in_order()
    {
        var current = new List<FakeTag> { _work, _design, _games };

        var result = TagOrdering.WithTagToggled(Workspace, current, _design.Id, assign: false);

        Assert.NotNull(result);
        Assert.Equal(new[] { "Work", "Games" }, result.Select(t => t.Name));
    }

    [Fact]
    public void Assigning_a_tag_the_item_already_has_reports_no_change()
    {
        // Null is what lets the caller skip both the write and the save.
        var current = new List<FakeTag> { _work };

        Assert.Null(TagOrdering.WithTagToggled(Workspace, current, _work.Id, assign: true));
    }

    [Fact]
    public void Removing_a_tag_the_item_does_not_have_reports_no_change()
        => Assert.Null(TagOrdering.WithTagToggled(Workspace, new List<FakeTag>(), _work.Id, assign: false));

    // --- Normalize ---

    [Fact]
    public void Normalize_reports_no_change_for_an_untagged_item()
        => Assert.Null(TagOrdering.Normalize(Workspace, Array.Empty<FakeTag>()));

    [Fact]
    public void Normalize_reports_no_change_when_already_correct()
        => Assert.Null(TagOrdering.Normalize(Workspace, new[] { _work, _design }));

    [Fact]
    public void Normalize_reorders_into_workspace_order()
    {
        var result = TagOrdering.Normalize(Workspace, new[] { _games, _work });

        Assert.NotNull(result);
        Assert.Equal(new[] { "Work", "Games" }, result.Select(t => t.Name));
    }

    [Fact]
    public void Normalize_drops_tags_the_workspace_no_longer_has()
    {
        var deleted = new FakeTag("Deleted");

        var result = TagOrdering.Normalize(Workspace, new[] { _work, deleted });

        Assert.NotNull(result);
        Assert.Equal("Work", Assert.Single(result).Name);
    }

    [Fact]
    public void Normalize_removes_duplicates()
    {
        var result = TagOrdering.Normalize(Workspace, new[] { _work, _work, _design });

        Assert.NotNull(result);
        Assert.Equal(new[] { "Work", "Design" }, result.Select(t => t.Name));
    }

    [Fact]
    public void Normalize_returns_the_shared_workspace_instances()
    {
        // Tags are shared objects: a rename or recolour has to propagate through the item's
        // own change notifications, which only works if it is holding the same instance.
        var workspace = Workspace;
        var result = TagOrdering.Normalize(workspace, new[] { new FakeTag("Work", _work.Id) });

        Assert.NotNull(result);
        Assert.Same(workspace[0], Assert.Single(result));
    }
}
