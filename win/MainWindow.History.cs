using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.System;

namespace ace_run;

/// <summary>
/// Back navigation across folders.
///
/// One step = one folder change, "Ungrouped" included. Search is not a step: Esc already
/// owns "leave the search", and giving Back a second meaning would make it unpredictable
/// which of the two things a press was going to do. Back therefore always returns to the
/// previous folder, leaving search mode on the way, exactly as clicking the rail would.
///
/// There is no forward stack. A launcher is not a browser — nothing here rewards
/// retracing a path forwards, and the affordance would sit disabled permanently.
///
/// History is per-session and per-workspace: it is dropped in ResetContentState(), the
/// one place that means "we are leaving this workspace".
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Visited folders, oldest first; null = ungrouped, matching <c>_selectedFolder</c>'s
    /// own convention. Ids rather than FolderViewModel references so a deleted folder
    /// leaves no live object stranded in here — <see cref="GoBack"/> resolves each entry
    /// against <c>_folders</c> when it pops it.
    /// </summary>
    private readonly List<Guid?> _folderHistory = new();

    /// <summary>
    /// Doubles as "a programmatic selection is in progress" — the same role
    /// <c>_suppressWorkspaceSwitch</c> plays for the workspace picker.
    /// <see cref="NavigateToFolder"/> assigns SidebarListView.SelectedItem itself, and the
    /// SelectionChanged that fires must not be mistaken for a user click and recorded as a
    /// second step (or, worse, re-entered).
    /// </summary>
    private bool _suppressFolderNavigation;

    // Deep enough that Back never runs out mid-session, bounded so a long session cannot
    // grow the list without limit.
    private const int MaxHistoryDepth = 32;

    /// <summary>
    /// Both back gestures, registered the same way and for the same reason: the controls
    /// they travel through mark the event handled before it reaches RootGrid. ListViewBase
    /// claims PointerPressed for its own selection, and claims the arrow keys for focus
    /// movement — so a plain XAML handler would see neither XButton1 nor Alt+Left anywhere
    /// the user actually is. handledEventsToo is the only registration that still fires.
    /// </summary>
    private void InitializeNavigationHistory()
    {
        RootGrid.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler(RootGrid_PointerPressed),
            handledEventsToo: true);

        RootGrid.AddHandler(
            UIElement.KeyDownEvent,
            new KeyEventHandler(RootGrid_BackKeyDown),
            handledEventsToo: true);
    }

    #region Navigation

    /// <summary>
    /// The single way the content area changes folder. Every entry point routes through
    /// here — rail click, the ungrouped row, "Go to folder" from a search result, deleting
    /// the open folder, and restoring the saved folder on load — so that recording a step
    /// is one decision in one place rather than five that have to agree.
    ///
    /// <paramref name="record"/> is false for moves the user did not ask for: restoring
    /// where they left off, and being pushed out of a folder that no longer exists.
    /// </summary>
    private void NavigateToFolder(FolderViewModel? target, bool record = true)
    {
        if (record) RecordNavigation(target);

        _suppressFolderNavigation = true;
        try
        {
            _selectedFolder = target;
            // Assign the selection before the header row's flag, the order the rail's own
            // handlers have always used: SelectedItem = null raises SelectionChanged, and
            // "ungrouped is selected" should not be true for the span of that callback.
            SidebarListView.SelectedItem = target;
            UngroupedItem.IsSelected = target is null;

            // Leaving search must precede RefreshContentArea, which reads _searchText for
            // the empty state — and the grid is collapsed while results are up, so a
            // refresh alone would leave the old result list on screen.
            ExitSearchMode();
            RefreshContentArea();
        }
        finally
        {
            _suppressFolderNavigation = false;
        }

        // Where we just landed may be what the next entry down points at — most obviously
        // after being evicted from a deleted folder back to ungrouped, when ungrouped is
        // already sitting on top of the stack.
        TrimHistory();
    }

    /// <summary>Records the folder being left, if this is a genuine move.</summary>
    private void RecordNavigation(FolderViewModel? target)
    {
        if (_suppressFolderNavigation) return;
        // Re-clicking the folder already open is not a step; recording it would make the
        // first Back appear to do nothing.
        if (ReferenceEquals(target, _selectedFolder)) return;

        _folderHistory.Add(_selectedFolder?.Id);
        if (_folderHistory.Count > MaxHistoryDepth)
            _folderHistory.RemoveAt(0);

        UpdateBackButtonState();
    }

    private void GoBack()
    {
        // TrimHistory has already dropped anything on top that Back could not act on, so
        // the first entry is normally the answer. The loop is the backstop.
        while (_folderHistory.Count > 0)
        {
            var id = _folderHistory[^1];
            _folderHistory.RemoveAt(_folderHistory.Count - 1);
            if (!IsNavigableTarget(id)) continue;

            NavigateToFolder(id is Guid folderId ? _folders.First(f => f.Id == folderId) : null,
                             record: false);
            return; // NavigateToFolder trims the new top on the way out
        }

        UpdateBackButtonState();
    }

    /// <summary>
    /// Would pressing Back on this entry actually move the user? False for a folder that
    /// has since been deleted, and false for the place they are already standing.
    /// </summary>
    private bool IsNavigableTarget(Guid? id) =>
        id is Guid folderId
            ? _selectedFolder?.Id != folderId && _folders.Any(f => f.Id == folderId)
            : _selectedFolder is not null;

    /// <summary>
    /// Enforces the one invariant this feature rests on: <b>if the button is enabled, a
    /// press moves.</b> Anything else means a press that visibly does nothing, which reads
    /// as the button being broken — and then a second press jumps two folders at once,
    /// because the first one silently spent an entry.
    ///
    /// Two things put a dead entry on top. A folder deleted while it sat in the history
    /// (handled at the delete, but only for that one id), and a navigation the user did not
    /// ask for landing on the entry that was already there — being evicted from a deleted
    /// folder back to ungrouped, when ungrouped is what Back was pointing at.
    /// </summary>
    private void TrimHistory()
    {
        while (_folderHistory.Count > 0 && !IsNavigableTarget(_folderHistory[^1]))
            _folderHistory.RemoveAt(_folderHistory.Count - 1);

        UpdateBackButtonState();
    }

    /// <summary>
    /// Drops a deleted folder from the history, wherever it sits. Entries deeper down are
    /// not TrimHistory's job — it only guarantees the top — and leaving them would make a
    /// later press land nowhere.
    /// </summary>
    private void PruneHistory(Guid deletedFolderId)
    {
        _folderHistory.RemoveAll(id => id == deletedFolderId);
        TrimHistory();
    }

    /// <summary>
    /// Folders belong to a workspace, so the history cannot outlive one. Called from
    /// ResetContentState, which also covers the reload after workspace management — where
    /// folders may have been renamed, reordered, imported or removed wholesale.
    /// </summary>
    private void ClearHistory()
    {
        _folderHistory.Clear();
        UpdateBackButtonState();
    }

    // Kept enabled/disabled rather than shown/hidden: collapsing the button would shift the
    // pane toggle and the workspace picker sideways every time the history empties.
    private void UpdateBackButtonState() =>
        AppTitleBar.IsBackButtonEnabled = _folderHistory.Count > 0;

    #endregion

    #region Input

    private void AppTitleBar_BackRequested(TitleBar sender, object args)
    {
        if (IsModal) return;
        GoBack();
    }

    /// <summary>
    /// Alt+Left. Not a <see cref="KeyboardAccelerator"/> like the other modified keys:
    /// Alt-modified accelerators never fire, because Alt arrives as WM_SYSKEYDOWN and the
    /// accelerator engine does not route it — the same finding that put Alt+Enter in the
    /// lists' own key handlers.
    /// </summary>
    private void RootGrid_BackKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Left || !e.KeyStatus.IsMenuKeyDown) return;
        if (IsModal) return;

        e.Handled = true;
        GoBack();
    }

    private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(RootGrid).Properties.IsXButton1Pressed) return;
        if (IsModal) return;

        e.Handled = true;
        GoBack();
    }

    #endregion
}
