using System;
using System.Collections.Generic;
using ace_run.Models;

namespace ace_run.Services;

/// <summary>
/// The most-recently-launched list, kept most recent first.
/// </summary>
/// <remarks>
/// Order is the point, not just membership: search reads the position as a sort key, so an
/// item launched two minutes ago outranks one launched an hour ago. The list is also what
/// fills the tray menu, which is why it is bounded — a tray menu is not a history browser.
/// </remarks>
public static class RecentLaunchList
{
    /// <summary>
    /// How many entries survive. Bounded because this list drives the tray menu, and because
    /// it is carried in every workspace file.
    /// </summary>
    public const int MaxRecent = 10;

    /// <summary>
    /// Moves <paramref name="app"/> to the front, adding it if it was not there.
    /// </summary>
    /// <remarks>
    /// Remove-then-insert rather than a search-and-move: an id must appear at most once, which
    /// is what lets search treat the index as a rank without checking for duplicates.
    /// </remarks>
    public static void Track(List<RecentLaunch> recents, IAppItemView app, int max = MaxRecent)
    {
        recents.RemoveAll(r => r.AppId == app.Id);
        recents.Insert(0, new RecentLaunch
        {
            AppId = app.Id,
            DisplayName = app.DisplayName,
            FilePath = app.FilePath
        });

        if (recents.Count > max)
            recents.RemoveRange(max, recents.Count - max);
    }

    /// <summary>
    /// Drops entries whose item no longer exists — deleted, or gone with a deleted folder.
    /// </summary>
    /// <returns>True when anything was removed, so the caller can skip a needless save.</returns>
    public static bool Purge(List<RecentLaunch> recents, ICollection<Guid> liveIds)
    {
        var before = recents.Count;
        recents.RemoveAll(r => !liveIds.Contains(r.AppId));
        return recents.Count < before;
    }
}
