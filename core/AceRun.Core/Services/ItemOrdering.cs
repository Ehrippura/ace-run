using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ace_run.Models;

namespace ace_run.Services;

/// <summary>
/// Criteria offered by the Organize submenu. Never persisted — a sort is applied once and the
/// resulting order becomes the manual order — so these names are free to change.
/// </summary>
public enum OrganizeBy
{
    Name,
    Path,
    Tag,
    SortKey
}

/// <summary>
/// One-shot reordering of an item collection. There is no persistent sort mode: the result is
/// just a new manual order, so drag-reorder keeps working and items added later still land at
/// the end.
/// </summary>
public static class ItemOrdering
{
    /// <summary>
    /// The order <paramref name="items"/> should be rearranged into.
    /// </summary>
    /// <param name="tagOrder">
    /// Workspace tag ids in the order the user arranged them, which is what
    /// <see cref="OrganizeBy.Tag"/> ranks against.
    /// </param>
    /// <remarks>
    /// <c>OrderBy</c> rather than <c>List.Sort</c>: LINQ's sort is stable, so items comparing
    /// equal on every key keep the order the user dragged them into. Same reasoning as
    /// <see cref="SearchRanking.Rank"/>.
    /// </remarks>
    public static List<T> Order<T>(IReadOnlyList<T> items, OrganizeBy by, IReadOnlyList<Guid> tagOrder)
        where T : IAppItemView
        => by switch
        {
            OrganizeBy.Name => items
                .OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),

            // A path is machine text, not display text, so it sorts by ordinal — matching how
            // Windows itself compares paths. URL items compare on the URL.
            OrganizeBy.Path => items
                .OrderBy(a => a.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),

            OrganizeBy.Tag => items
                .OrderBy(a => PrimaryTagRank(a, tagOrder))
                .ThenBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),

            // Unkeyed items trail the keyed ones: an item nobody has classified should not
            // lead the folder just because an empty string sorts first.
            OrganizeBy.SortKey => items
                .OrderBy(a => a.SortKey.Length == 0 ? 1 : 0)
                .ThenBy(a => a.SortKey, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),

            _ => items.ToList()
        };

    /// <summary>
    /// Position of an item's primary tag in <paramref name="tagOrder"/>, or
    /// <see cref="int.MaxValue"/> when it carries none — or carries one the workspace no
    /// longer has.
    /// </summary>
    /// <remarks>
    /// An item's tags are kept in workspace order by tag normalization, so the first one is
    /// the tag the user ranked highest in the manage-tags dialog. Sorting by tag *name*
    /// instead would fight that ordering.
    /// </remarks>
    public static int PrimaryTagRank(IAppItemView item, IReadOnlyList<Guid> tagOrder)
    {
        // First tag only, and only if the workspace still lists it.
        foreach (var tag in item.Tags)
        {
            var index = IndexOf(tagOrder, tag.Id);
            return index < 0 ? int.MaxValue : index;
        }

        return int.MaxValue;
    }

    private static int IndexOf(IReadOnlyList<Guid> ids, Guid id)
    {
        for (var i = 0; i < ids.Count; i++)
            if (ids[i] == id)
                return i;

        return -1;
    }

    /// <summary>
    /// Rearranges <paramref name="target"/> in place to match <paramref name="ordered"/>.
    /// </summary>
    /// <returns>True when anything actually moved, so the caller can skip a needless save.</returns>
    /// <remarks>
    /// Moves rather than Clear + Add. A Clear raises a Reset, which recycles every GridView
    /// container: the container-content-changing handler would release each icon and reload it
    /// on re-realization, so the whole grid blinks. Move raises a Move, which the GridView
    /// repositions without recycling. IndexOf makes this O(n²), which is fine for the tens of
    /// items a folder holds.
    /// </remarks>
    public static bool ApplyOrder<T>(ObservableCollection<T> target, IReadOnlyList<T> ordered)
    {
        var moved = false;

        for (var i = 0; i < ordered.Count; i++)
        {
            var current = target.IndexOf(ordered[i]);
            if (current == i) continue;

            target.Move(current, i);
            moved = true;
        }

        return moved;
    }

    /// <summary>
    /// Shifts one item by <paramref name="delta"/> positions, clamped by the collection's bounds.
    /// </summary>
    /// <returns>
    /// True when the item actually moved. False means the caller has nothing to persist —
    /// the item is absent, <paramref name="delta"/> is zero, or it is already at that end.
    /// </returns>
    /// <remarks>
    /// This is the keyboard half of drag reordering, and <c>Move</c> is what makes it usable
    /// twice in a row. A <c>ListView</c>'s own drag reorder mutates its source with
    /// <c>RemoveAt</c> + <c>Insert</c>, which recycles containers: the row's container is torn
    /// down and a different one is prepared for the item, so focus does not survive and a
    /// second "move up" would have nothing focused to act on. <c>Move</c> raises a Move
    /// notification, which repositions the existing container — same reasoning as
    /// <see cref="ApplyOrder"/>, for a different symptom.
    /// </remarks>
    public static bool MoveBy<T>(ObservableCollection<T> target, T item, int delta)
    {
        if (delta == 0) return false;

        var from = target.IndexOf(item);
        if (from < 0) return false;

        var to = from + delta;
        if (to < 0 || to >= target.Count) return false;

        target.Move(from, to);
        return true;
    }
}
