using System;
using System.Collections.Generic;
using System.Linq;
using ace_run.Models;

namespace ace_run.Services;

/// <summary>
/// Projecting a set of tag ids back onto the workspace's tag list, in the workspace's order.
/// </summary>
/// <remarks>
/// The fixed order is what lets the tag dots on two tiles carrying the same tags line up, and
/// it also means there is no per-item tag ordering for the user to manage. Rebuilding through
/// the workspace list rather than appending to the item's own is what keeps that order for
/// free — and it drops ids of tags that have since been deleted, and de-duplicates, in the
/// same pass.
///
/// This was open-coded at four call sites before it lived here.
/// </remarks>
public static class TagOrdering
{
    /// <summary>
    /// The subset of <paramref name="workspaceTags"/> named by <paramref name="ids"/>, in
    /// workspace order.
    /// </summary>
    public static List<T> InWorkspaceOrder<T>(IEnumerable<T> workspaceTags, ICollection<Guid> ids)
        where T : ITagRef
        => workspaceTags.Where(t => ids.Contains(t.Id)).ToList();

    /// <summary>
    /// The tags an item should end up with after <paramref name="tagId"/> is added or removed.
    /// </summary>
    /// <returns>
    /// Null when the item is already in the requested state, so the caller can skip both the
    /// write and the save.
    /// </returns>
    public static List<T>? WithTagToggled<T>(
        IEnumerable<T> workspaceTags,
        IEnumerable<T> current,
        Guid tagId,
        bool assign) where T : ITagRef
    {
        var assigned = new HashSet<Guid>(current.Select(t => t.Id));

        // HashSet.Add / Remove report whether they changed anything, which is the
        // already-in-the-requested-state test.
        if (assign ? !assigned.Add(tagId) : !assigned.Remove(tagId))
            return null;

        return InWorkspaceOrder(workspaceTags, assigned);
    }

    /// <summary>
    /// The tags an item should end up with once ids the workspace no longer has are dropped,
    /// duplicates removed, and the rest put back into workspace order.
    /// </summary>
    /// <returns>
    /// Null when the item's tags already satisfy all three, so nothing needs writing.
    /// </returns>
    public static List<T>? Normalize<T>(IEnumerable<T> workspaceTags, IReadOnlyList<T> current)
        where T : ITagRef
    {
        if (current.Count == 0) return null;

        var assigned = new HashSet<Guid>(current.Select(t => t.Id));
        var ordered = InWorkspaceOrder(workspaceTags, assigned);

        return ordered.SequenceEqual(current) ? null : ordered;
    }
}
