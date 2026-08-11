using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ace_run.Services;

namespace ace_run;

public sealed partial class MainWindow
{
    #region Organize

    /// <summary>
    /// Reorders one item collection by the given criterion, once, and persists the result.
    /// The ordering itself lives in <see cref="ItemOrdering"/>; what stays here is the
    /// workspace state it needs and the save it triggers.
    /// </summary>
    private void Organize(ObservableCollection<AppItemViewModel> target, OrganizeBy by)
    {
        if (target.Count < 2) return;

        ApplyOrder(target, ItemOrdering.Order(target, by, TagOrderIds()));
    }

    private void ReverseOrder(ObservableCollection<AppItemViewModel> target)
    {
        if (target.Count < 2) return;
        ApplyOrder(target, target.Reverse().ToList());
    }

    /// <summary>Workspace tag ids in the user's arrangement — what "by tag" ranks against.</summary>
    private List<System.Guid> TagOrderIds() => _tags.Select(t => t.Id).ToList();

    private void ApplyOrder(ObservableCollection<AppItemViewModel> target, IReadOnlyList<AppItemViewModel> ordered)
    {
        if (!ItemOrdering.ApplyOrder(target, ordered)) return;

        // CommitSave, not SaveItems: the rail is right-clickable while a search is running
        // and SaveItems early-returns there. Same reason the two DragItemsCompleted handlers
        // commit directly. The bound collection instance is unchanged, so the GridView needs
        // no refresh.
        CommitSave();
    }

    #endregion
}
