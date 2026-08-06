using ace_run.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace ace_run;

public sealed partial class MainWindow
{
    #region Sidebar

    /// <summary>
    /// Doubles as "a programmatic selection is in progress" — the same role
    /// <c>_suppressWorkspaceSwitch</c> plays for the workspace picker.
    /// <see cref="NavigateToFolder"/> assigns SidebarListView.SelectedItem itself, and the
    /// SelectionChanged that fires must not be re-entered as a fresh navigation.
    /// </summary>
    private bool _suppressFolderNavigation;

    /// <summary>
    /// The single way the content area changes folder. Every entry point routes through
    /// here — rail click, the ungrouped row, "Go to folder" from a search result, deleting
    /// the open folder, and restoring the saved folder on load — because the five of them
    /// had drifted into five slightly different orderings of the same five steps.
    /// </summary>
    private void NavigateToFolder(FolderViewModel? target)
    {
        _suppressFolderNavigation = true;
        try
        {
            _selectedFolder = target;
            // Assign the selection before the header row's flag, the order the rail's own
            // handlers have always used: SelectedItem = null raises SelectionChanged, and
            // "ungrouped is selected" should not be true for the span of that callback.
            SidebarListView.SelectedItem = target;
            UngroupedItem.IsSelected = target is null;

            // Picking a folder means leaving search: the grid is collapsed while results are
            // up, so refreshing it alone would leave the old result list on screen — and
            // ReleaseHiddenIcons() would strip the icons off the very items being shown.
            // Must run before RefreshContentArea, which reads _searchText for the empty state.
            ExitSearchMode();
            RefreshContentArea();
        }
        finally
        {
            _suppressFolderNavigation = false;
        }
    }

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
    /// Name, path, launch arguments and tag names, all case-insensitive substring. Path is
    /// matched so a URL item is findable by its domain; arguments so two entries pointing at
    /// the same exe can be told apart; tag names so a tag doubles as a query.
    /// Tags are matched one by one rather than against <c>TagsSummary</c> — that string is
    /// joined with a separator, so a query could otherwise match across two tag names.
    /// </summary>
    private static bool MatchesQuery(AppItemViewModel app, string query) =>
        app.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
        || app.FilePath.Contains(query, StringComparison.OrdinalIgnoreCase)
        || app.Arguments.Contains(query, StringComparison.OrdinalIgnoreCase)
        || app.Tags.Any(t => t.Name.Contains(query, StringComparison.OrdinalIgnoreCase));

    private void RunSearch()
    {
        _searchDebounce?.Stop();
        _searchPending = false;

        var query = _searchText;
        if (string.IsNullOrEmpty(query)) return;

        _searchResults.Clear();

        var ungroupedLabel = Loc.GetString("UngroupedFolderName");
        foreach (var app in _ungroupedApps)
            if (MatchesQuery(app, query))
            {
                app.FolderLabel = ungroupedLabel;
                _searchResults.Add(app);
            }
        foreach (var folder in _folders)
            foreach (var app in folder.Apps)
                if (MatchesQuery(app, query))
                {
                    app.FolderLabel = folder.DisplayName;
                    _searchResults.Add(app);
                }

        foreach (var app in _searchResults)
            _ = app.LoadIconAsync();

        // Pre-select the top hit so Enter has a visible target. Selection only — focus stays
        // in the search box, so typing carries on uninterrupted and the row renders in the
        // "Selected Unfocused" state. Setting this before the containers are realized is
        // fine: SelectedIndex selects on the data, and the visual follows on realization.
        SearchResultsView.SelectedIndex = _searchResults.Count > 0 ? 0 : -1;

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

    private void SearchResultsView_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement fe) return;

        var lvi = FindParent<ListViewItem>(fe);
        if (lvi is null || SearchResultsView.ItemFromContainer(lvi) is not AppItemViewModel app)
            return;

        var flyout = new MenuFlyout();
        var goToFolderItem = new MenuFlyoutItem
        {
            Text = Loc.GetString("Search_GoToFolder"),
            Icon = new FontIcon { Glyph = "\uE8B7" }
        };
        goToFolderItem.Click += (_, _) => NavigateToAppFolder(app);
        flyout.Items.Add(goToFolderItem);

        ShowTrackedFlyout(flyout, fe, new FlyoutShowOptions { Position = e.GetPosition(fe) });
        e.Handled = true;
    }

    /// <summary>Clears the search and switches the content area to the folder that
    /// contains <paramref name="app"/> (or the ungrouped page), then selects it.</summary>
    private void NavigateToAppFolder(AppItemViewModel app)
    {
        NavigateToFolder(FindFolderOfApp(app));

        AppGridView.SelectedItem = app;
        AppGridView.ScrollIntoView(app);
    }

    private async void SearchResultsView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            if (SearchResultsView.SelectedItem is AppItemViewModel app)
                await LaunchOrEditAsync(app, e);
        }
        else if (e.Key == Windows.System.VirtualKey.Delete)
        {
            e.Handled = true;
            var targets = SearchResultsView.SelectedItems.Cast<AppItemViewModel>().ToList();
            await DeleteAppsAsync(targets);
        }
    }

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

    private void AppGridView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        CommitSave();
    }

    private void AppGridView_DragOver(object sender, DragEventArgs e)
    {
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
        var deferral = e.GetDeferral();
        try
        {
            // Browsers offer several formats for the same link, so take the first that works
            // rather than adding the item once per format.
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
                await DropStorageItemsAsync(e.DataView);
            else if (e.DataView.Contains(StandardDataFormats.WebLink))
                await DropWebLinkAsync(e.DataView);
            else if (e.DataView.Contains(StandardDataFormats.Text))
                await DropTextAsync(e.DataView);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async Task DropStorageItemsAsync(DataPackageView dataView)
    {
        var storageItems = await dataView.GetStorageItemsAsync();
        foreach (var storageItem in storageItems.OfType<StorageFile>())
        {
            // .url Internet Shortcut from the desktop
            if (storageItem.FileType.Equals(".url", StringComparison.OrdinalIgnoreCase))
            {
                var shortcutUrl = UrlUtil.ReadInternetShortcut(storageItem.Path);
                if (shortcutUrl is not null && UrlUtil.TryNormalize(shortcutUrl, out var normalized))
                    AddUrlDirectly(normalized);
                continue;
            }

            var filePath = storageItem.Path;

            if (storageItem.FileType.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                filePath = ResolveLnkTarget(storageItem.Path) ?? storageItem.Path;

            if (filePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(filePath))
                AddItemDirectly(filePath);
        }
    }

    private async Task DropWebLinkAsync(DataPackageView dataView)
    {
        var uri = await dataView.GetWebLinkAsync();
        if (UrlUtil.TryNormalize(uri?.AbsoluteUri, out var url))
            AddUrlDirectly(url);
    }

    /// <summary>Covers dragging out of the address bar, which offers text but no WebLink.</summary>
    private async Task DropTextAsync(DataPackageView dataView)
    {
        var text = await dataView.GetTextAsync();
        if (UrlUtil.TryNormalize(text, out var url))
            AddUrlDirectly(url);
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
            if (AppGridView.SelectedItem is AppItemViewModel app)
                await LaunchOrEditAsync(app, e);
        }
        else if (e.Key == Windows.System.VirtualKey.Delete)
        {
            e.Handled = true;
            var targets = AppGridView.SelectedItems.Cast<AppItemViewModel>().ToList();
            await DeleteAppsAsync(targets);
        }
    }

    #endregion

    #region Context Menus

    private void AppGridView_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement fe) return;

        var gvi = FindParent<GridViewItem>(fe);
        AppItemViewModel? tappedApp = null;
        if (gvi is not null)
            tappedApp = AppGridView.ItemFromContainer(gvi) as AppItemViewModel;

        var selectedApps = AppGridView.SelectedItems.Cast<AppItemViewModel>().ToList();
        bool isMultiSelect = selectedApps.Count > 1 && tappedApp is not null && selectedApps.Contains(tappedApp);

        if (tappedApp is null) return;

        if (!selectedApps.Contains(tappedApp))
        {
            AppGridView.SelectedItem = tappedApp;
            selectedApps = new List<AppItemViewModel> { tappedApp };
            isMultiSelect = false;
        }

        var flyout = new MenuFlyout();

        if (isMultiSelect)
        {
            var launchAllItem = new MenuFlyoutItem
            {
                Text = Loc.GetString("LaunchAllMenuItem"),
                Icon = new FontIcon { Glyph = "\uE768" }
            };
            var capturedApps = selectedApps.ToList();
            launchAllItem.Click += (_, _) =>
            {
                foreach (var app in capturedApps)
                    LaunchApp(app);
            };
            flyout.Items.Add(launchAllItem);

            flyout.Items.Add(new MenuFlyoutSeparator());

            var deleteMultiItem = new MenuFlyoutItem
            {
                Text = string.Format(Loc.GetString("DeleteSelectedMenuItem"), selectedApps.Count),
                Icon = new FontIcon { Glyph = "\uE74D" }
            };
            var capturedApps2 = selectedApps.ToList();
            deleteMultiItem.Click += async (_, _) => await DeleteAppsAsync(capturedApps2);
            flyout.Items.Add(deleteMultiItem);
        }
        else if (tappedApp is not null)
        {
            var app = tappedApp;

            var launchItem = new MenuFlyoutItem
            {
                Text = Loc.GetString("LaunchMenuItem"),
                Icon = new FontIcon { Glyph = "\uE768" }
            };
            launchItem.Click += (_, _) => LaunchApp(app);
            flyout.Items.Add(launchItem);

            flyout.Items.Add(new MenuFlyoutSeparator());

            // Display-only hint. The accelerator itself lives on RootGrid: a flyout that
            // has never been opened has no realized visual tree, so an accelerator
            // attached here would never fire.
            var editItem = new MenuFlyoutItem
            {
                Text = Loc.GetString("EditMenuItem.Text"),
                Icon = new FontIcon { Glyph = "\uE70F" },
                KeyboardAcceleratorTextOverride = "Alt+Enter",
                Tag = app
            };
            editItem.Click += EditApp_Click;
            flyout.Items.Add(editItem);

            // "Open File Location" is meaningless for a URL \u2014 offer the link instead.
            if (app.IsUrl)
            {
                var copyUrlItem = new MenuFlyoutItem
                {
                    Text = Loc.GetString("CopyUrlMenuItem"),
                    Icon = new FontIcon { Glyph = "\uE71B" },
                    Tag = app
                };
                copyUrlItem.Click += CopyUrl_Click;
                flyout.Items.Add(copyUrlItem);
            }
            else
            {
                var openFolderItem = new MenuFlyoutItem
                {
                    Text = Loc.GetString("OpenFolderMenuItem.Text"),
                    Icon = new FontIcon { Glyph = "\uE838" },
                    Tag = app
                };
                openFolderItem.Click += OpenFolder_Click;
                flyout.Items.Add(openFolderItem);
            }

            flyout.Items.Add(new MenuFlyoutSeparator());

            var moveToMenu = new MenuFlyoutSubItem
            {
                Text = Loc.GetString("MoveToMenuItem"),
                Icon = new FontIcon { Glyph = "\uE8DE" }
            };

            var moveToUngrouped = new MenuFlyoutItem
            {
                Text = Loc.GetString("UngroupedFolderName")
            };
            moveToUngrouped.Click += (_, _) => MoveAppTo(app, null);
            moveToMenu.Items.Add(moveToUngrouped);

            foreach (var folder in _folders)
            {
                var folderCapture = folder;
                var moveToFolder = new MenuFlyoutItem { Text = folder.DisplayName };
                moveToFolder.Click += (_, _) => MoveAppTo(app, folderCapture);
                moveToMenu.Items.Add(moveToFolder);
            }

            flyout.Items.Add(moveToMenu);

            var setTagMenu = new MenuFlyoutSubItem
            {
                Text = Loc.GetString("Tag_Set"),
                Icon = new FontIcon { Glyph = "\uE8EC" }
            };

            var assignedTagIds = new HashSet<Guid>(app.Tags.Select(t => t.Id));

            var clearTagsItem = new MenuFlyoutItem
            {
                Text = Loc.GetString("Tag_Clear"),
                IsEnabled = assignedTagIds.Count > 0
            };
            clearTagsItem.Click += (_, _) => ClearTagsOnApp(app);
            setTagMenu.Items.Add(clearTagsItem);
            setTagMenu.Items.Add(new MenuFlyoutSeparator());

            foreach (var tag in _tags)
            {
                var tagCapture = tag;
                // A flyout closes on click, so assigning several tags this way means
                // reopening the menu. The edit dialog is the path for doing it in one go.
                var tagItem = new ToggleMenuFlyoutItem
                {
                    Text = tag.Name,
                    IsChecked = assignedTagIds.Contains(tag.Id),
                    Icon = new FontIcon { Glyph = "\uEA3B", Foreground = tag.ColorBrush }
                };
                tagItem.Click += (s, _) =>
                    ToggleTagOnApp(app, tagCapture, ((ToggleMenuFlyoutItem)s).IsChecked);
                setTagMenu.Items.Add(tagItem);
            }

            flyout.Items.Add(setTagMenu);

            flyout.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem
            {
                Text = Loc.GetString("DeleteMenuItem.Text"),
                Icon = new FontIcon { Glyph = "\uE74D" },
                KeyboardAcceleratorTextOverride = "Del"
            };
            deleteItem.Click += async (_, _) => await DeleteAppsAsync(new[] { app });
            flyout.Items.Add(deleteItem);
        }

        ShowTrackedFlyout(flyout, fe, new FlyoutShowOptions { Position = e.GetPosition(fe) });
        e.Handled = true;
    }

    private void SidebarListView_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement fe) return;

        var lvi = FindParent<ListViewItem>(fe);
        if (lvi is null) return;

        var folder = SidebarListView.ItemFromContainer(lvi) as FolderViewModel;
        if (folder is null) return;

        var flyout = new MenuFlyout();

        var renameItem = new MenuFlyoutItem
        {
            Text = Loc.GetString("RenameFolder"),
            Icon = new FontIcon { Glyph = "\uE8AC" },
            KeyboardAcceleratorTextOverride = "F2"
        };
        renameItem.Click += async (_, _) => await RenameFolderAsync(folder);
        flyout.Items.Add(renameItem);

        flyout.Items.Add(new MenuFlyoutSeparator());

        var deleteItem = new MenuFlyoutItem
        {
            Text = Loc.GetString("DeleteFolder"),
            Icon = new FontIcon { Glyph = "\uE74D" },
            KeyboardAcceleratorTextOverride = "Del"
        };
        deleteItem.Click += async (_, _) => await DeleteFolderAsync(folder);
        flyout.Items.Add(deleteItem);

        ShowTrackedFlyout(flyout, fe, new FlyoutShowOptions { Position = e.GetPosition(fe) });
        e.Handled = true;
    }

    #endregion
}
