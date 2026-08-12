using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using System;
using System.Threading.Tasks;
using Windows.System;

namespace ace_run;

/// <summary>
/// Keyboard support. Two mechanisms, chosen by key shape:
///
/// Modified keys (Ctrl / Ctrl+Shift / Ctrl+Alt / Alt) are <see cref="KeyboardAccelerator"/>s
/// on RootGrid — genuinely global, and safe to fire while a text box has focus.
///
/// Unmodified single keys (Esc, F2, Delete, Down) are KeyDown handlers on the specific
/// control instead. A global unmodified accelerator would also fire while SearchBox has
/// focus, so a global Delete would open the delete-apps prompt while the user is editing
/// a search string. That is disqualifying, not a preference.
///
/// RootGrid hosts the accelerators because it is an ancestor of every focusable element in
/// the window, so invocation resolves along the focus chain rather than relying on the
/// framework's whole-tree fallback. It is also never collapsed or disabled, unlike the
/// rail's contents (which close below 800 DIP) or flyout items (whose visual tree does not
/// exist until the flyout has been opened once).
/// </summary>
public sealed partial class MainWindow
{
    #region Modal guard

    // WinUI allows only one ContentDialog at a time. Every dialog entry point used to be
    // mouse-only, so nothing could race; accelerators make the race reachable.
    private int _modalDepth;
    private bool IsModal => _modalDepth > 0;

    /// <summary>
    /// Shows a dialog with the modal guard held for its lifetime.
    ///
    /// Also the one place the theme override reaches a dialog: a ContentDialog is hosted on
    /// the popup layer rather than inside the element tree that carries RequestedTheme, so
    /// it would otherwise come up in the system theme while the window behind it is in the
    /// chosen one. Every dialog in the app goes through here for exactly that reason.
    /// </summary>
    private async Task<ContentDialogResult> ShowModalAsync(ContentDialog dialog)
    {
        dialog.RequestedTheme = Services.ThemeService.ToElementTheme(App.CurrentTheme);

        _modalDepth++;
        try { return await dialog.ShowAsync(); }
        finally { _modalDepth--; }
    }

    /// <summary>
    /// Holds the guard across a whole flow rather than a single dialog — for sequences
    /// that would otherwise leave a gap between two modals (picker then edit dialog) or
    /// that keep mutating state after the dialog closes (workspace management reload).
    /// </summary>
    private async Task RunModalAsync(Func<Task> body)
    {
        _modalDepth++;
        try { await body(); }
        finally { _modalDepth--; }
    }

    /// <summary>
    /// Counts an open flyout as modal. Flyouts live on the popup layer, which does *not*
    /// suppress RootGrid's accelerators — verified by hand: Ctrl+2 switched workspace with
    /// the manage menu open, leaving a stale menu floating over the new workspace's
    /// content, and its entries would then have acted on a workspace the user never
    /// opened it from.
    /// </summary>
    private void TrackAsModal(FlyoutBase? flyout)
    {
        if (flyout is null) return;
        flyout.Opened += (_, _) => _modalDepth++;
        flyout.Closed += (_, _) => _modalDepth--;
    }

    /// <summary>Shows a transient (code-built) flyout with the modal guard attached.</summary>
    private void ShowTrackedFlyout(FlyoutBase flyout, FrameworkElement target, FlyoutShowOptions options)
    {
        TrackAsModal(flyout);
        flyout.ShowAt(target, options);
    }

    #endregion

    #region Accelerators declared in code

    // Windows.System.VirtualKey has no name for the comma key.
    private const VirtualKey VirtualKeyComma = (VirtualKey)0xBC; // VK_OEM_COMMA

    /// <summary>
    /// The accelerators XAML handles badly: Ctrl+1..9 (nine near-identical blocks, and a
    /// closure captures the index instead of the handler decoding VirtualKey) and Ctrl+,
    /// (no named VirtualKey to write in markup).
    /// </summary>
    private void InstallCodeAccelerators()
    {
        for (var i = 0; i < 9; i++)
        {
            var index = i;
            AddAccelerator(
                (VirtualKey)((int)VirtualKey.Number1 + i),
                VirtualKeyModifiers.Control,
                () => SelectWorkspaceByIndex(index));
        }

        AddAccelerator(VirtualKeyComma, VirtualKeyModifiers.Control, OpenSettingsWindow);

        // The two persistent title-bar flyouts. Transient right-click menus get the same
        // treatment at their ShowAt call sites via ShowTrackedFlyout.
        TrackAsModal(SettingsButton.Flyout);
        TrackAsModal(AddButton.Flyout);
    }

    private void AddAccelerator(VirtualKey key, VirtualKeyModifiers modifiers, Action action)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += (_, args) =>
        {
            args.Handled = true;
            action();
        };
        RootGrid.KeyboardAccelerators.Add(accelerator);
    }

    private void SelectWorkspaceByIndex(int index)
    {
        if (IsModal) return;

        // Fewer workspaces than the digit pressed: stay inert. Clamping to the last one
        // would make Ctrl+7 and Ctrl+9 do the same thing, which is worse than nothing.
        if (index < 0 || index >= _workspaces.Count) return;

        // Set the picker, not SwitchWorkspaceAsync directly: WorkspaceComboBox_SelectionChanged
        // performs the switch and the ComboBox display stays in sync for free. Re-selecting
        // the current item raises no SelectionChanged, so a repeated Ctrl+1 costs nothing.
        WorkspaceComboBox.SelectedItem = _workspaces[index];
    }

    #endregion

    #region Accelerator handlers

    private void FocusSearch_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (IsModal) return;
        FocusSearchBox();
    }

    /// <summary>
    /// Puts the caret in the search box with any existing query selected, so the next
    /// keystroke replaces it. Shared by Ctrl+F and by the global hotkey, which focuses the
    /// box after summoning the window — arriving at a launcher and having to reach for the
    /// mouse before typing defeats the point of the hotkey.
    ///
    /// <c>Focus</c> alone is not enough, whatever <see cref="FocusState"/> it is given: it
    /// lands the caret but leaves the selection collapsed at the end of the old text, so
    /// summoning onto yesterday's "steam" and typing "chrome" produced "steamchrome".
    /// Verified by hand — this comment used to claim Keyboard focus selected the query, and
    /// it never did.
    ///
    /// The selection is made on the inner TextBox because AutoSuggestBox exposes no
    /// selection API of its own, and that TextBox is a template part rather than a named
    /// child of this page. Selecting rather than clearing is deliberate: a summon that only
    /// wanted to look at the previous results again still finds them on screen.
    /// </summary>
    public void FocusSearchBox()
    {
        SearchBox.Focus(FocusState.Keyboard);

        if (FindDescendant<TextBox>(SearchBox) is { } inner)
            inner.SelectAll();
    }

    private async void AddApp_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (IsModal) return;
        await PickAndAddAppAsync();
    }

    private async void AddUrl_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (IsModal) return;
        await AddUrlAsync(string.Empty);
    }

    private async void NewFolder_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (IsModal) return;
        await NewFolderAsync();
    }

    private void ToggleRail_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (IsModal) return;
        ToggleRail();
    }

    /// <summary>
    /// Ctrl+, opens Settings directly — the convention the shortcut carries everywhere else.
    /// It used to open the gear menu instead, which made the app's one standard shortcut
    /// land on a menu the user then had to arrow through; workspaces and tags are still one
    /// click away behind the gear.
    ///
    /// The modal guard still applies even though SettingsWindow is a top-level window rather
    /// than a ContentDialog: raising it over an open dialog would leave a modal the user
    /// cannot see behind a window they can. <c>App.ShowSettings</c> owns the single-instance
    /// rule, so a repeat press just brings the open one forward.
    /// </summary>
    private void OpenSettingsWindow()
    {
        if (IsModal) return;
        ((App)Application.Current).ShowSettings(this);
    }

    #endregion

    #region Unmodified keys

    /// <summary>
    /// Enter launches the whole selection, Alt+Enter edits (Explorer's "Properties" gesture).
    ///
    /// Alt+Enter is handled here rather than as a <see cref="KeyboardAccelerator"/> on
    /// RootGrid like every other modified key: an Alt-modified accelerator never fired in
    /// testing. Alt+Enter arrives as WM_SYSKEYDOWN, which the accelerator engine does not
    /// route. Both lists already own Enter via PreviewKeyDown, so branching on the
    /// modifier costs nothing and works.
    /// </summary>
    private async Task LaunchOrEditAsync(ListViewBase list, KeyRoutedEventArgs e)
    {
        var apps = SelectedAppsInOrder(list);
        if (apps.Count == 0) return;

        if (e.KeyStatus.IsMenuKeyDown)
        {
            // There is no batch form of "edit" — EditItemDialog edits one item. Rather than
            // pick an arbitrary member of the selection, a multi-selection does nothing.
            if (apps.Count == 1)
                await EditAppAsync(apps[0]);
        }
        else
        {
            LaunchApps(apps);
        }
    }

    /// <summary>
    /// Escape, handled most-local-first. Deliberately does not hide the window. The
    /// original reason — no global hotkey, so the tray icon would be the only way back and
    /// the app would read as having vanished — no longer holds for everyone, but the
    /// hotkey is off by default and Esc cannot branch on whether one is bound without
    /// becoming unpredictable. Hiding on Esc is a settings-gated behaviour change, not a
    /// side effect of this key.
    /// </summary>
    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;

        if (!string.IsNullOrEmpty(SearchBox.Text))
        {
            ClearSearch();
            e.Handled = true;
            return;
        }

        if (RailSplitView.DisplayMode == SplitViewDisplayMode.Overlay && RailSplitView.IsPaneOpen)
        {
            RailSplitView.IsPaneOpen = false;
            e.Handled = true;
        }
    }

    private async void SidebarListView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // The "Ungrouped" row is a ListView.Header, not a FolderViewModel, so SelectedItem
        // is null while it is active — it must not be renamable or deletable.
        if (SidebarListView.SelectedItem is not FolderViewModel folder) return;

        if (e.Key == VirtualKey.F2)
        {
            e.Handled = true;
            await RenameFolderAsync(folder);
        }
        else if (e.Key == VirtualKey.Delete)
        {
            e.Handled = true;
            await DeleteFolderAsync(folder);
        }
    }

    /// <summary>Enter in the search box launches the top hit — what a launcher should do.</summary>
    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        FlushPendingSearch(); // typing fast and hitting Enter must not launch a stale top hit

        // The top hit is pre-selected, but a click or Down can move the highlight elsewhere
        // and focus can come back to the box — Enter has to launch whatever looks selected.
        if (SearchResultsView.SelectedItem is AppItemViewModel selected)
            LaunchApp(selected);
        else if (_searchResults.Count > 0)
            LaunchApp(_searchResults[0]);
    }

    /// <summary>
    /// Down hands focus to the results list, where Enter / Delete already work; Escape
    /// clears the query.
    ///
    /// PreviewKeyDown, not KeyDown: AutoSuggestBox consumes both keys internally for its
    /// suggestion list, so a bubbling handler never sees them — verified by hand, Down did
    /// nothing until this moved to the tunneling event. Escape is handled here as well as
    /// in RootGrid_KeyDown, which still covers the case where focus has already moved on
    /// to the results list.
    /// </summary>
    private void SearchBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            if (string.IsNullOrEmpty(SearchBox.Text)) return;
            ClearSearch();
            e.Handled = true;
            return;
        }

        if (e.Key != VirtualKey.Down) return;

        FlushPendingSearch(); // the list has to exist before focus can move into it
        if (_searchResults.Count == 0) return;

        SelectOnly(SearchResultsView, _searchResults[0]);
        if (SearchResultsView.ContainerFromIndex(0) is ListViewItem item)
            item.Focus(FocusState.Keyboard);
        else
            SearchResultsView.Focus(FocusState.Keyboard);

        e.Handled = true;
    }

    private void ClearSearch()
    {
        ExitSearchMode();
        AppGridView.Focus(FocusState.Programmatic);
    }

    #endregion
}
