using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ace_run;

public sealed partial class MainWindow
{
    /// <summary>
    /// Criteria offered by the Organize submenu. Never persisted — a sort is applied once
    /// and the resulting order becomes the manual order — so these names are free to change.
    /// </summary>
    private enum OrganizeBy
    {
        Name,
        Path,
        Tag,
        SortKey
    }

    #region Organize

    /// <summary>
    /// Reorders one item collection by the given criterion, once. There is no persistent
    /// sort mode: the result is just a new manual order, so drag-reorder keeps working and
    /// items added later still land at the end.
    /// </summary>
    private void Organize(ObservableCollection<AppItemViewModel> target, OrganizeBy by)
    {
        if (target.Count < 2) return;

        // OrderBy rather than List.Sort: LINQ's sort is stable, so items comparing equal on
        // every key keep the order the user dragged them into. Same reasoning as RunSearch.
        var ordered = by switch
        {
            OrganizeBy.Name => target
                .OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),

            // A path is machine text, not display text, so it sorts by ordinal — matching
            // how Windows itself compares paths. URL items compare on the URL.
            OrganizeBy.Path => target
                .OrderBy(a => a.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),

            OrganizeBy.Tag => target
                .OrderBy(PrimaryTagRank)
                .ThenBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),

            // Unkeyed items trail the keyed ones: an item nobody has classified should not
            // lead the folder just because an empty string sorts first.
            OrganizeBy.SortKey => target
                .OrderBy(a => a.SortKey.Length == 0 ? 1 : 0)
                .ThenBy(a => a.SortKey, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),

            _ => null
        };

        if (ordered is not null)
            ApplyOrder(target, ordered);
    }

    private void ReverseOrder(ObservableCollection<AppItemViewModel> target)
    {
        if (target.Count < 2) return;
        ApplyOrder(target, target.Reverse().ToList());
    }

    /// <summary>
    /// Position of an item's primary tag in the workspace tag list, or <see cref="int.MaxValue"/>
    /// when it carries none. <see cref="AppItemViewModel.Tags"/> is kept in workspace order by
    /// <c>NormalizeAppTags</c>, so the first tag is the one the user ranked highest in the
    /// manage-tags dialog — sorting by tag name instead would fight that ordering.
    /// </summary>
    private int PrimaryTagRank(AppItemViewModel app)
    {
        if (app.Tags.Count == 0) return int.MaxValue;

        var index = _tags.IndexOf(app.Tags[0]);
        return index < 0 ? int.MaxValue : index;
    }

    /// <summary>
    /// Rearranges <paramref name="target"/> in place to match <paramref name="ordered"/>,
    /// then saves — unless nothing moved.
    /// </summary>
    /// <remarks>
    /// Moves rather than Clear + Add. A Clear raises a Reset, which recycles every GridView
    /// container: <c>AppGridView_ContainerContentChanging</c> would release each icon and
    /// reload it on re-realization, so the whole grid blinks. Move raises a Move, which the
    /// GridView repositions without recycling. IndexOf makes this O(n²), which is fine for
    /// the tens of items a folder holds.
    /// </remarks>
    private void ApplyOrder(ObservableCollection<AppItemViewModel> target, IReadOnlyList<AppItemViewModel> ordered)
    {
        var moved = false;

        for (var i = 0; i < ordered.Count; i++)
        {
            var current = target.IndexOf(ordered[i]);
            if (current == i) continue;

            target.Move(current, i);
            moved = true;
        }

        if (!moved) return;

        // CommitSave, not SaveItems: the rail is right-clickable while a search is running
        // and SaveItems early-returns there. Same reason the two DragItemsCompleted handlers
        // commit directly. The bound collection instance is unchanged, so the GridView needs
        // no refresh.
        CommitSave();
    }

    #endregion
}
