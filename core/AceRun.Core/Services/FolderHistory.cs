using System;
using System.Collections.Generic;

namespace ace_run.Services;

/// <summary>
/// The back stack for folder navigation.
/// </summary>
/// <remarks>
/// <para>
/// One step = one folder change, "Ungrouped" included, which is why entries are
/// <see cref="Guid"/>? — null means ungrouped, matching the convention the selected-folder
/// field uses.
/// </para>
/// <para>
/// Ids rather than folder objects, so a folder deleted while it sits in the history leaves no
/// live object stranded here; every entry is re-resolved as it is popped.
/// </para>
/// <para>
/// The invariant everything else here serves: <b>if Back is enabled, pressing it moves.</b>
/// Anything else means a press that visibly does nothing, which reads as a broken button —
/// and then a second press jumps two folders at once, because the first silently spent an
/// entry. <see cref="Trim"/> is what enforces it, and callers must run it after any change to
/// where the user is standing or to which folders exist.
/// </para>
/// <para>
/// There is no forward stack. A launcher is not a browser; the affordance would sit disabled
/// permanently.
/// </para>
/// </remarks>
public sealed class FolderHistory
{
    // Deep enough that Back never runs out mid-session, bounded so a long session cannot grow
    // the list without limit.
    public const int MaxDepth = 32;

    private readonly List<Guid?> _entries = new();

    public int Count => _entries.Count;

    public bool CanGoBack => _entries.Count > 0;

    /// <summary>Pushes the folder being left. Oldest entries are evicted past <see cref="MaxDepth"/>.</summary>
    public void Record(Guid? leaving)
    {
        _entries.Add(leaving);
        if (_entries.Count > MaxDepth)
            _entries.RemoveAt(0);
    }

    /// <summary>
    /// Would pressing Back on this entry actually move the user? False for a folder that has
    /// since been deleted, and false for the place they are already standing.
    /// </summary>
    public static bool IsNavigable(Guid? entry, Guid? current, Func<Guid, bool> folderExists)
        => entry is Guid id
            ? current != id && folderExists(id)
            : current is not null;

    /// <summary>
    /// Drops dead entries from the top of the stack. Two things put one there: a folder
    /// deleted while it sat in the history, and a navigation the user did not ask for landing
    /// on the entry that was already there — being evicted from a deleted folder back to
    /// ungrouped, when ungrouped is what Back was pointing at.
    /// </summary>
    public void Trim(Guid? current, Func<Guid, bool> folderExists)
    {
        while (_entries.Count > 0 && !IsNavigable(_entries[^1], current, folderExists))
            _entries.RemoveAt(_entries.Count - 1);
    }

    /// <summary>
    /// Pops the most recent entry the user can actually be taken to.
    /// </summary>
    /// <returns>False when nothing navigable remains, in which case the stack is now empty.</returns>
    /// <remarks>
    /// <see cref="Trim"/> has normally already dropped anything unusable, so the first entry
    /// is the answer; the loop is the backstop.
    /// </remarks>
    public bool TryGoBack(Guid? current, Func<Guid, bool> folderExists, out Guid? target)
    {
        while (_entries.Count > 0)
        {
            var entry = _entries[^1];
            _entries.RemoveAt(_entries.Count - 1);

            if (!IsNavigable(entry, current, folderExists)) continue;

            target = entry;
            return true;
        }

        target = null;
        return false;
    }

    /// <summary>
    /// Drops a deleted folder from the history, wherever it sits. Entries deeper down are not
    /// <see cref="Trim"/>'s job — it only guarantees the top — and leaving them would make a
    /// later press land nowhere.
    /// </summary>
    public void Prune(Guid deletedFolderId, Guid? current, Func<Guid, bool> folderExists)
    {
        _entries.RemoveAll(id => id == deletedFolderId);
        Trim(current, folderExists);
    }

    /// <summary>
    /// Folders belong to a workspace, so the history cannot outlive one.
    /// </summary>
    public void Clear() => _entries.Clear();
}
