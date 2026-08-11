using ace_run.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;

namespace ace_run;

public sealed partial class MainWindow
{
    #region Sidebar

    private void SidebarListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // NavigateToFolder assigns SelectedItem itself, so this also fires for switches
        // already in progress. Those are an echo, not a new navigation.
        if (_suppressFolderNavigation) return;

        if (SidebarListView.SelectedItem is FolderViewModel folder)
            NavigateToFolder(folder);
    }

    private void UngroupedItem_Tapped(object sender, TappedRoutedEventArgs e) =>
        NavigateToFolder(null);

    /// <summary>
    /// Clicking the folder that is *already* selected raises no SelectionChanged, so it needs
    /// its own way out of search mode — otherwise clicking the current folder while results
    /// are on screen looks like nothing happened. Only runs while a search is active; a plain
    /// re-click must not re-assign AppGridView.ItemsSource (that would reset scroll position).
    /// </summary>
    private void SidebarListView_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // Empty means either no search, or SelectionChanged already left search mode.
        if (string.IsNullOrEmpty(_searchText)) return;
        if (e.OriginalSource is not FrameworkElement fe) return;

        var lvi = FindParent<ListViewItem>(fe);
        if (lvi is null || SidebarListView.ItemFromContainer(lvi) is not FolderViewModel folder)
            return;
        if (!ReferenceEquals(folder, _selectedFolder)) return;

        ExitSearchMode();
        RefreshContentArea();
    }

    private void SidebarListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        CommitSave();
    }

    /// <summary>
    /// Resolves the rail row under a drag. Returns false when the pointer is not over a drop
    /// target at all; on true, a null <paramref name="folder"/> means the "Ungrouped" row.
    ///
    /// The row is found by hit-testing the pointer, *not* by walking up from
    /// <c>e.OriginalSource</c>. On a drag event the OriginalSource is the ListView itself
    /// rather than the row under the cursor — verified by logging every DragOver during a
    /// real drag — so the FindParent pattern used by the Tapped and RightTapped handlers
    /// silently resolves nothing here and every drop gets refused.
    /// </summary>
    private bool TryResolveRailDropTarget(DragEventArgs e, out FolderViewModel? folder)
    {
        folder = null;

        // GetPosition(null) is relative to the XamlRoot, which is the coordinate space
        // FindElementsInHostCoordinates expects. Results come back topmost-first.
        var hit = VisualTreeHelper
            .FindElementsInHostCoordinates(e.GetPosition(null), SidebarListView)
            .OfType<ListViewItem>()
            .FirstOrDefault();
        if (hit is null) return false;

        // "Ungrouped" is the ListView.Header rather than a collection item, so there is
        // nothing to look up — the container *is* the target.
        if (ReferenceEquals(hit, UngroupedItem)) return true;

        if (SidebarListView.ItemFromContainer(hit) is not FolderViewModel target) return false;
        folder = target;
        return true;
    }

    /// <summary>
    /// Accepts app tiles dragged onto a rail row. The rail had AllowDrop="True" for its own
    /// folder reordering but no drop handler, so tiles dragged here simply did nothing.
    ///
    /// A drag that is not carrying app items returns without touching AcceptedOperation or
    /// Handled — that is what leaves the ListView's built-in folder-reorder drag working.
    /// </summary>
    private void SidebarListView_DragOver(object sender, DragEventArgs e)
    {
        if (_draggedApps is not { Count: > 0 }) return;

        if (!TryResolveRailDropTarget(e, out var folder)
            || ReferenceEquals(folder, _selectedFolder))
        {
            // Dropping into the folder already on screen would remove and re-append every
            // item, silently reordering the view the user is looking at. Refuse instead.
            // ReferenceEquals also covers ungrouped-onto-ungrouped, where both are null.
            // The highlight is cleared too, so a refused row never looks like a live target.
            ClearRailDropHighlight();
            e.AcceptedOperation = DataPackageOperation.None;
            e.Handled = true;
            return;
        }

        HighlightRailDropTarget(folder);

        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.Caption = string.Format(
            Loc.GetString("DragMoveCaption"),
            folder?.DisplayName ?? Loc.GetString("UngroupedFolderName"));
        e.DragUIOverride.IsGlyphVisible = true;
        e.Handled = true;
    }

    /// <summary>Clears the highlight when the pointer leaves the rail entirely.</summary>
    private void SidebarListView_DragLeave(object sender, DragEventArgs e) =>
        ClearRailDropHighlight();

    private void SidebarListView_Drop(object sender, DragEventArgs e)
    {
        ClearRailDropHighlight();

        if (_draggedApps is not { Count: > 0 } apps) return;
        if (!TryResolveRailDropTarget(e, out var folder)) return;
        if (ReferenceEquals(folder, _selectedFolder)) return;

        MoveAppsTo(apps, folder);
        _draggedApps = null;
        e.Handled = true;
    }

    /// <summary>
    /// The rail row currently lit up as the drop target. Held as the *folder* rather than
    /// the container so it survives container recycling mid-drag; null while the ungrouped
    /// header is the target, which is why <see cref="_railDropIsUngrouped"/> exists — null
    /// alone cannot tell "the ungrouped row" apart from "nothing".
    /// </summary>
    private FolderViewModel? _railDropFolder;
    private bool _railDropIsUngrouped;

    private void HighlightRailDropTarget(FolderViewModel? folder)
    {
        var isUngrouped = folder is null;
        if (ReferenceEquals(folder, _railDropFolder) && isUngrouped == _railDropIsUngrouped)
            return; // same row, nothing to repaint

        ClearRailDropHighlight();

        if (folder is not null)
            folder.IsDropTarget = true;
        else
            UngroupedDropHighlight.Visibility = Visibility.Visible;

        _railDropFolder = folder;
        _railDropIsUngrouped = isUngrouped;
    }

    private void ClearRailDropHighlight()
    {
        if (_railDropFolder is not null)
            _railDropFolder.IsDropTarget = false;
        if (_railDropIsUngrouped)
            UngroupedDropHighlight.Visibility = Visibility.Collapsed;

        _railDropFolder = null;
        _railDropIsUngrouped = false;
    }

    #endregion

    #region Search

    /// <summary>
    /// How long typing has to settle before the filter runs. A pass walks every item in the
    /// workspace and starts an icon load per hit — work the intermediate states of a word
    /// being typed do not need.
    /// </summary>
    private const int SearchDebounceMs = 180;

    private void InitializeSearch()
    {
        _searchDebounce = DispatcherQueue.CreateTimer();
        _searchDebounce.Interval = TimeSpan.FromMilliseconds(SearchDebounceMs);
        _searchDebounce.IsRepeating = false;
        _searchDebounce.Tick += (_, _) => RunSearch();
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        _searchText = sender.Text;

        if (string.IsNullOrEmpty(_searchText))
        {
            // Emptying the box is never debounced — the grid has to come back at once.
            _searchDebounce?.Stop();
            _searchPending = false;
            SearchResultsView.Visibility = Visibility.Collapsed;
            AppGridView.Visibility = Visibility.Visible;
            _searchResults.Clear();
            return;
        }

        // The mode switch stays immediate; only the result list waits. _searchText doubles
        // as the "search is active" flag for SaveItems / UpdateEmptyState, so it must lead.
        AppGridView.Visibility = Visibility.Collapsed;
        SearchResultsView.Visibility = Visibility.Visible;

        // _searchResults is deliberately left alone here: clearing it up front would flash
        // the "no results" placeholder between every keystroke. The previous hits stay on
        // screen until the new pass replaces them, and _searchPending holds the placeholder
        // back for the first character, where there is nothing to keep showing.
        _searchPending = true;
        _searchDebounce?.Stop();
        _searchDebounce?.Start();
    }

    /// <summary>
    /// Every item in the workspace, each paired with the folder label a search result would
    /// show. Traversal order is the arrangement the user dragged into place, and
    /// <see cref="SearchRanking.Rank"/> uses it as its last tiebreak.
    /// </summary>
    private IEnumerable<SearchCandidate<AppItemViewModel>> SearchCandidates()
    {
        var ungroupedLabel = Loc.GetString("UngroupedFolderName");
        foreach (var app in _ungroupedApps)
            yield return new SearchCandidate<AppItemViewModel>(app, ungroupedLabel);

        foreach (var folder in _folders)
            foreach (var app in folder.Apps)
                yield return new SearchCandidate<AppItemViewModel>(app, folder.DisplayName);
    }

    private void RunSearch()
    {
        _searchDebounce?.Stop();
        _searchPending = false;

        var query = _searchText;
        if (string.IsNullOrEmpty(query)) return;

        _searchResults.Clear();

        // Ranked before anything is added rather than added and reordered: the collection is
        // bound to SearchResultsView, so filling it in the wrong order would churn containers
        // for no reason. FolderLabel is assigned from the result rather than during ranking,
        // which keeps the ranking pass free of side effects on the items it is ranking.
        foreach (var hit in SearchRanking.Rank(query, SearchCandidates(), _appData?.RecentLaunches))
        {
            hit.Item.FolderLabel = hit.FolderLabel;
            _searchResults.Add(hit.Item);
        }

        // Icons are not loaded here — SearchResultsView_ContainerContentChanging loads them
        // per realized row, so a query matching 200 items does not start 200 disk reads for
        // the ~10 rows that are actually on screen.

        // Pre-select the top hit so Enter has a visible target. Selection only — focus stays
        // in the search box, so typing carries on uninterrupted and the row renders in the
        // "Selected Unfocused" state. Setting this before the containers are realized is
        // fine: the selection is on the data, and the visual follows on realization.
        SelectOnly(SearchResultsView, _searchResults.Count > 0 ? _searchResults[0] : null);

        // _searchPending suppressed the placeholder while the pass was queued.
        UpdateEmptyState();
    }

    /// <summary>
    /// Runs a queued pass right now. Enter and Down act on <c>_searchResults</c>, so they
    /// must not be served the previous keystroke's list — or an empty one on the first
    /// character, which would make a fast "type and hit Enter" launch nothing at all.
    /// </summary>
    private void FlushPendingSearch()
    {
        if (_searchPending)
            RunSearch();
    }

    /// <summary>
    /// Leaves search mode: empties the query box, drops the result list and puts the grid
    /// back in front.
    ///
    /// The state is reset here instead of being left to SearchBox_TextChanged because
    /// AutoSuggestBox raises TextChanged asynchronously — a caller that goes on to touch
    /// AppGridView (select an item, refresh its source) would otherwise be acting on a
    /// still-collapsed grid. TextChanged does fire afterwards with an empty query and takes
    /// the same branch, so running both is harmless.
    /// </summary>
    private void ExitSearchMode()
    {
        if (string.IsNullOrEmpty(_searchText) && string.IsNullOrEmpty(SearchBox.Text))
            return;

        // A queued pass would otherwise fire after the folder switch and re-enter search mode.
        _searchDebounce?.Stop();
        _searchPending = false;

        _searchText = string.Empty;
        SearchBox.Text = string.Empty;
        _searchResults.Clear();
        SearchResultsView.Visibility = Visibility.Collapsed;
        AppGridView.Visibility = Visibility.Visible;
    }

    private void SearchResultsView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement fe)
        {
            var lvi = FindParent<ListViewItem>(fe);
            if (lvi is not null && SearchResultsView.ItemFromContainer(lvi) is AppItemViewModel app)
            {
                LaunchApp(app);
                e.Handled = true;
            }
        }
    }

    private void SearchResultsView_RightTapped(object sender, RightTappedRoutedEventArgs e) =>
        ShowAppMenu(SearchResultsView, e);

    /// <summary>Clears the search and switches the content area to the folder that
    /// contains <paramref name="app"/> (or the ungrouped page), then selects it.</summary>
    private void NavigateToAppFolder(AppItemViewModel app)
    {
        NavigateToFolder(FindFolderOfApp(app));

        SelectOnly(AppGridView, app);
        AppGridView.ScrollIntoView(app);
    }

    private async void SearchResultsView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            await LaunchOrEditAsync(SearchResultsView, e);
        }
        else if (e.Key == Windows.System.VirtualKey.Delete)
        {
            e.Handled = true;
            await DeleteAppsAsync(SelectedAppsInOrder(SearchResultsView));
        }
    }

    #endregion

    #region Selection

    /// <summary>
    /// Replaces the selection with one item, or clears it when <paramref name="item"/> is
    /// null. Under SelectionMode="Extended", assigning SelectedItem / SelectedIndex is not a
    /// reliable *replace* \u2014 it can add to what is already selected \u2014 so every programmatic
    /// "select just this one" goes through here.
    /// </summary>
    private static void SelectOnly(ListViewBase list, object? item)
    {
        list.SelectedItems.Clear();
        if (item is not null)
            list.SelectedItems.Add(item);
    }

    /// <summary>
    /// The selected apps in the order they appear on screen.
    ///
    /// SelectedItems is in *selection* order. Without this, a batch move would land items in
    /// whatever sequence the user happened to Ctrl+click, and Launch All would fire in that
    /// order too \u2014 neither of which the user can see or predict from the screen.
    /// </summary>
    private static List<AppItemViewModel> SelectedAppsInOrder(ListViewBase list) =>
        list.SelectedItems
            .OfType<AppItemViewModel>()
            .OrderBy(app => list.Items.IndexOf(app))
            .ToList();

    #endregion

    #region GridView Events

    private void AppGridView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement fe)
        {
            var gvi = FindParent<GridViewItem>(fe);
            if (gvi is not null && AppGridView.ItemFromContainer(gvi) is AppItemViewModel app)
            {
                LaunchApp(app);
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// The apps currently being dragged out of the grid.
    ///
    /// The rail's drop handlers need two things the DataPackage cannot give them: the items
    /// themselves (a ListViewBase's internal reorder payload has no public reader) and a way
    /// to tell an app drag apart from the rail's own folder-reorder drag. This field answers
    /// both — null means "not an app drag, keep your hands off the event".
    /// </summary>
    private List<AppItemViewModel>? _draggedApps;

    private void AppGridView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        // Extended selection drags the whole selection, so e.Items already holds all of it.
        _draggedApps = e.Items.OfType<AppItemViewModel>().ToList();

        // Both lists are ListViewBase with CanReorderItems, which switches on WinUI's
        // built-in cross-list item transfer: the rail decides these tiles are items it
        // should *insert*, opens a gap between two rows and offers an insertion line. That
        // is wrong twice over — a folder row is a container to drop *into*, and an
        // AppItemViewModel has no business being inserted into _folders — and the gap also
        // breaks the drop itself, because the hit test in TryResolveRailDropTarget then
        // lands on the ScrollViewer between the parted rows and resolves nothing, which
        // freezes the drag caption on whichever row last succeeded.
        //
        // Turning reorder off for the duration of an app drag removes all of it. AllowDrop
        // stays on, so our own handlers still run, and the rail's own folder reordering is
        // untouched: a folder drag never comes through here.
        SidebarListView.CanReorderItems = false;
    }

    private void AppGridView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        // Fires after the rail's Drop handler, so clearing here cannot cut a move short.
        // Also fires when the drag is cancelled, so the rail always gets reorder back and
        // never keeps a stale highlight after a drag that ended anywhere else.
        SidebarListView.CanReorderItems = true;
        ClearRailDropHighlight();
        _draggedApps = null;
        CommitSave();
    }

    private void AppGridView_DragOver(object sender, DragEventArgs e)
    {
        // Our own tiles being reordered. The GridView drives that internally and its package
        // carries a text representation of the items, which would otherwise read here as an
        // external text drop and get answered with the "add to Ace Run" caption.
        if (_draggedApps is { Count: > 0 }) return;

        if (e.DataView.Contains(StandardDataFormats.StorageItems)
            || e.DataView.Contains(StandardDataFormats.WebLink)
            || e.DataView.Contains(StandardDataFormats.Text))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = Loc.GetString("DragDropCaption");
            e.DragUIOverride.IsGlyphVisible = true;
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
    }

    private async void AppGridView_Drop(object sender, DragEventArgs e)
    {
        if (_draggedApps is { Count: > 0 }) return; // our own reorder, handled by the GridView

        // Resolved before the first await: the position on the args is only meaningful while
        // the event is being raised, and reading a dropped file can take long enough for the
        // collection to be looked at again.
        var index = ResolveGridDropIndex(e);

        var deferral = e.GetDeferral();
        try
        {
            // Browsers offer several formats for the same link, so take the first that works
            // rather than adding the item once per format.
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
                await DropStorageItemsAsync(e.DataView, index);
            else if (e.DataView.Contains(StandardDataFormats.WebLink))
                await DropWebLinkAsync(e.DataView, index);
            else if (e.DataView.Contains(StandardDataFormats.Text))
                await DropTextAsync(e.DataView, index);
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>
    /// Where a drop onto the grid should insert, matching the insertion line the GridView
    /// draws while the pointer is over it. Dropped items used to be appended regardless, so
    /// the indicator promised a position the drop did not honour.
    ///
    /// Measured against the tiles rather than hit-tested through them, because the two things
    /// a hit test can land on during a drag are both dead ends: the OriginalSource of a drag
    /// event is the list itself (see <see cref="TryResolveRailDropTarget"/>), and the grid
    /// parts its tiles to open the insertion gap — so the one position the user is most
    /// likely to drop on contains no container at all. Walking the realized containers in
    /// index order answers "which tile does the pointer come before" in either layout.
    ///
    /// Past the last tile, on the empty-state panel, or over a grid whose containers are not
    /// realized yet, the loop falls through and appends — which is what the indicator shows
    /// in those places too.
    /// </summary>
    private int ResolveGridDropIndex(DragEventArgs e)
    {
        var count = (_selectedFolder?.Apps ?? _ungroupedApps).Count;
        var point = e.GetPosition(AppGridView);

        return DropGeometry.ResolveInsertIndex(point.X, point.Y, count, TileBoundsAt);
    }

    /// <summary>
    /// Bounds of a realized tile relative to the grid, or null when it is scrolled out and
    /// has no container.
    /// </summary>
    private TileBounds? TileBoundsAt(int index)
    {
        if (AppGridView.ContainerFromIndex(index) is not FrameworkElement container)
            return null;

        var origin = container.TransformToVisual(AppGridView).TransformPoint(new Point(0, 0));
        return new TileBounds(origin.X, origin.Y, container.ActualWidth, container.ActualHeight);
    }

    private async Task DropStorageItemsAsync(DataPackageView dataView, int index)
    {
        var storageItems = await dataView.GetStorageItemsAsync();
        foreach (var storageItem in storageItems.OfType<StorageFile>())
        {
            // index++ only where an item is really added, so a skipped file does not leave a
            // gap in the run — a multi-file drop lands as one block in the order given.
            // .url Internet Shortcut from the desktop
            if (storageItem.FileType.Equals(".url", StringComparison.OrdinalIgnoreCase))
            {
                var shortcutUrl = UrlUtil.ReadInternetShortcut(storageItem.Path);
                if (shortcutUrl is not null && UrlUtil.TryNormalize(shortcutUrl, out var normalized))
                    AddUrlDirectly(normalized, index++);
                continue;
            }

            var filePath = storageItem.Path;

            if (storageItem.FileType.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                filePath = ResolveLnkTarget(storageItem.Path) ?? storageItem.Path;

            if (filePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(filePath))
                AddItemDirectly(filePath, index++);
        }
    }

    private async Task DropWebLinkAsync(DataPackageView dataView, int index)
    {
        var uri = await dataView.GetWebLinkAsync();
        if (UrlUtil.TryNormalize(uri?.AbsoluteUri, out var url))
            AddUrlDirectly(url, index);
    }

    /// <summary>Covers dragging out of the address bar, which offers text but no WebLink.</summary>
    private async Task DropTextAsync(DataPackageView dataView, int index)
    {
        var text = await dataView.GetTextAsync();
        if (UrlUtil.TryNormalize(text, out var url))
            AddUrlDirectly(url, index);
    }

    private static string? ResolveLnkTarget(string lnkPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return null;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(lnkPath);
            return (string)shortcut.TargetPath;
        }
        catch
        {
            return null;
        }
    }

    private async void AppGridView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            await LaunchOrEditAsync(AppGridView, e);
        }
        else if (e.Key == Windows.System.VirtualKey.Delete)
        {
            e.Handled = true;
            await DeleteAppsAsync(SelectedAppsInOrder(AppGridView));
        }
    }

    #endregion

    #region Context Menus

    /// <summary>
    /// Shared right-click entry point for both item views.
    ///
    /// Resolves the tapped item, then collapses the selection onto it when the click landed
    /// outside the current selection — Explorer's behaviour, and the thing that makes a
    /// right-click on an unselected tile act on that tile rather than on whatever happened
    /// to be selected elsewhere. A click inside the selection leaves it intact, so the menu
    /// is built for the whole batch.
    ///
    /// Right-clicking empty space below the items shows no menu, as before.
    /// </summary>
    private void ShowAppMenu(ListViewBase list, RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement fe) return;

        // SelectorItem is the common base of GridViewItem and ListViewItem, so one lookup
        // serves the tile grid and the search rows alike.
        var container = FindParent<SelectorItem>(fe);
        if (container is null || list.ItemFromContainer(container) is not AppItemViewModel tapped)
            return;

        if (!list.SelectedItems.Contains(tapped))
            SelectOnly(list, tapped);

        var flyout = BuildAppMenu(list, SelectedAppsInOrder(list));

        // ShowTrackedFlyout, not ShowAt: an open flyout does not suppress RootGrid's
        // accelerators on its own, so it has to be folded into the modal guard.
        ShowTrackedFlyout(flyout, fe, new FlyoutShowOptions { Position = e.GetPosition(fe) });
        e.Handled = true;
    }

    private void AppGridView_RightTapped(object sender, RightTappedRoutedEventArgs e) =>
        ShowAppMenu(AppGridView, e);

    private void SidebarListView_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement fe) return;

        var lvi = FindParent<ListViewItem>(fe);
        if (lvi is null) return;

        // The Ungrouped row is the ListView.Header, so ItemFromContainer has nothing to hand
        // back for it \u2014 which is why right-clicking it used to do nothing at all. It gets
        // Organize only: there is no such folder to rename or delete.
        var folder = SidebarListView.ItemFromContainer(lvi) as FolderViewModel;
        if (folder is null && !ReferenceEquals(lvi, UngroupedItem)) return;

        var target = folder?.Apps ?? _ungroupedApps;
        var flyout = new MenuFlyout();

        if (folder is not null)
        {
            var renameItem = new MenuFlyoutItem
            {
                Text = Loc.GetString("RenameFolder"),
                Icon = new FontIcon { Glyph = "\uE8AC" },
                KeyboardAcceleratorTextOverride = "F2"
            };
            renameItem.Click += async (_, _) => await RenameFolderAsync(folder);
            flyout.Items.Add(renameItem);
        }

        flyout.Items.Add(BuildOrganizeSubmenu(target));

        if (folder is not null)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem
            {
                Text = Loc.GetString("DeleteFolder"),
                Icon = new FontIcon { Glyph = "\uE74D" },
                KeyboardAcceleratorTextOverride = "Del"
            };
            deleteItem.Click += async (_, _) => await DeleteFolderAsync(folder);
            flyout.Items.Add(deleteItem);
        }

        ShowTrackedFlyout(flyout, fe, new FlyoutShowOptions { Position = e.GetPosition(fe) });
        e.Handled = true;
    }

    /// <summary>
    /// The Organize submenu: four ascending criteria plus a reverse. Descending is covered
    /// by organizing then reversing, which keeps the menu one level deep.
    /// </summary>
    private MenuFlyoutSubItem BuildOrganizeSubmenu(ObservableCollection<AppItemViewModel> target)
    {
        var menu = new MenuFlyoutSubItem
        {
            Text = Loc.GetString("Organize_Menu"),
            Icon = new FontIcon { Glyph = "\uE8CB" },
            // Fewer than two items has no order to impose.
            IsEnabled = target.Count > 1
        };

        void AddCriterion(string key, OrganizeBy by)
        {
            var item = new MenuFlyoutItem { Text = Loc.GetString(key) };
            item.Click += (_, _) => Organize(target, by);
            menu.Items.Add(item);
        }

        AddCriterion("Organize_ByName", OrganizeBy.Name);
        AddCriterion("Organize_ByPath", OrganizeBy.Path);
        AddCriterion("Organize_ByTag", OrganizeBy.Tag);
        AddCriterion("Organize_BySortKey", OrganizeBy.SortKey);

        menu.Items.Add(new MenuFlyoutSeparator());

        var reverseItem = new MenuFlyoutItem { Text = Loc.GetString("Organize_Reverse") };
        reverseItem.Click += (_, _) => ReverseOrder(target);
        menu.Items.Add(reverseItem);

        return menu;
    }

    #endregion
}
