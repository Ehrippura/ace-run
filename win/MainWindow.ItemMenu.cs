using ace_run.Services;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System.Linq;

namespace ace_run;

/// <summary>
/// The right-click menu for app items, shared by the tile grid and the search results.
///
/// It lives here rather than inside either RightTapped handler because the two views must
/// offer the same commands — before this the grid had the full menu and the search results
/// had one entry, so "delete this" worked on a tile and did nothing on a row showing the
/// same item. The only difference the menu draws between them is "Go to Folder", which is
/// meaningless outside search.
///
/// Every entry acts on the whole selection. A one-item selection is not a special case in
/// the data flow, only in the wording and in the entries that cannot be batched: Edit opens
/// a dialog for one item, and Copy Link / Open File Location address one path.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Builds the menu for <paramref name="apps"/> — the current selection, in visual order.
    /// The caller is responsible for having collapsed the selection onto the tapped item
    /// first, so this never has to reconcile the two.
    /// </summary>
    private MenuFlyout BuildAppMenu(ListViewBase list, IList<AppItemViewModel> apps)
    {
        var flyout = new MenuFlyout();
        if (apps.Count == 0) return flyout;

        var captured = apps.ToList();
        var inSearch = ReferenceEquals(list, SearchResultsView);
        var single = captured.Count == 1 ? captured[0] : null;

        // Search results only. Ambiguous for a multi-selection whose items live in different
        // folders, so it is offered one item at a time.
        if (inSearch && single is not null)
        {
            var goToFolderItem = new MenuFlyoutItem
            {
                Text = Loc.GetString("Search_GoToFolder"),
                Icon = new FontIcon { Glyph = "\uE8B7" }
            };
            goToFolderItem.Click += (_, _) => NavigateToAppFolder(single);
            flyout.Items.Add(goToFolderItem);
            flyout.Items.Add(new MenuFlyoutSeparator());
        }

        var launchItem = new MenuFlyoutItem
        {
            Text = Loc.GetString(single is not null ? "LaunchMenuItem" : "LaunchAllMenuItem"),
            Icon = new FontIcon { Glyph = "\uE768" }
        };
        launchItem.Click += (_, _) => LaunchApps(captured);
        flyout.Items.Add(launchItem);

        flyout.Items.Add(new MenuFlyoutSeparator());

        if (single is not null)
        {
            // Display-only hint. The gesture itself is handled in the lists' PreviewKeyDown:
            // Alt+Enter arrives as WM_SYSKEYDOWN, which the accelerator engine does not route.
            var editItem = new MenuFlyoutItem
            {
                Text = Loc.GetString("EditMenuItem.Text"),
                Icon = new FontIcon { Glyph = "\uE70F" },
                KeyboardAcceleratorTextOverride = "Alt+Enter",
                Tag = single
            };
            editItem.Click += EditApp_Click;
            flyout.Items.Add(editItem);

            // "Open File Location" is meaningless for a URL — offer the link instead.
            if (single.IsUrl)
            {
                var copyUrlItem = new MenuFlyoutItem
                {
                    Text = Loc.GetString("CopyUrlMenuItem"),
                    Icon = new FontIcon { Glyph = "\uE71B" },
                    Tag = single
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
                    Tag = single
                };
                openFolderItem.Click += OpenFolder_Click;
                flyout.Items.Add(openFolderItem);
            }

            flyout.Items.Add(new MenuFlyoutSeparator());
        }

        flyout.Items.Add(BuildMoveToSubmenu(captured));
        flyout.Items.Add(BuildTagSubmenu(captured));

        flyout.Items.Add(new MenuFlyoutSeparator());

        var deleteItem = new MenuFlyoutItem
        {
            Text = single is not null
                ? Loc.GetString("DeleteMenuItem.Text")
                : string.Format(Loc.GetString("DeleteSelectedMenuItem"), captured.Count),
            Icon = new FontIcon { Glyph = "\uE74D" },
            KeyboardAcceleratorTextOverride = "Del"
        };
        deleteItem.Click += async (_, _) => await DeleteAppsAsync(captured);
        flyout.Items.Add(deleteItem);

        return flyout;
    }

    private MenuFlyoutSubItem BuildMoveToSubmenu(IList<AppItemViewModel> apps)
    {
        var moveToMenu = new MenuFlyoutSubItem
        {
            Text = Loc.GetString("MoveToMenuItem"),
            Icon = new FontIcon { Glyph = "\uE8DE" }
        };

        var moveToUngrouped = new MenuFlyoutItem
        {
            Text = Loc.GetString("UngroupedFolderName")
        };
        moveToUngrouped.Click += (_, _) => MoveAppsTo(apps, null);
        moveToMenu.Items.Add(moveToUngrouped);

        foreach (var folder in _folders)
        {
            var folderCapture = folder;
            var moveToFolder = new MenuFlyoutItem { Text = folder.DisplayName };
            moveToFolder.Click += (_, _) => MoveAppsTo(apps, folderCapture);
            moveToMenu.Items.Add(moveToFolder);
        }

        return moveToMenu;
    }

    private MenuFlyoutSubItem BuildTagSubmenu(IList<AppItemViewModel> apps)
    {
        var setTagMenu = new MenuFlyoutSubItem
        {
            Text = Loc.GetString("Tag_Set"),
            Icon = new FontIcon { Glyph = "\uE8EC" }
        };

        var clearTagsItem = new MenuFlyoutItem
        {
            Text = Loc.GetString("Tag_Clear"),
            IsEnabled = apps.Any(a => a.Tags.Count > 0)
        };
        clearTagsItem.Click += (_, _) => ClearTagsOnApps(apps);
        setTagMenu.Items.Add(clearTagsItem);

        if (_tags.Count > 0)
            setTagMenu.Items.Add(new MenuFlyoutSeparator());

        foreach (var tag in _tags)
        {
            var tagCapture = tag;

            // ToggleMenuFlyoutItem has no indeterminate state, so a selection where only some
            // items carry the tag has to read as one of the two. It reads as unchecked, which
            // makes the first click "give it to all of them" — the direction someone reaching
            // for a tag on a multi-selection almost always wants. Clicking again takes it off
            // all of them, so the mixed state is not recoverable through this menu; the edit
            // dialog stays the per-item surface.
            //
            // A flyout closes on invoke, so assigning several tags means reopening the menu.
            var tagItem = new ToggleMenuFlyoutItem
            {
                Text = tag.Name,
                IsChecked = apps.All(a => a.Tags.Any(t => t.Id == tagCapture.Id)),
                Icon = new FontIcon { Glyph = "\uEA3B", Foreground = tag.ColorBrush }
            };
            tagItem.Click += (s, _) =>
                SetTagOnApps(apps, tagCapture, ((ToggleMenuFlyoutItem)s).IsChecked);
            setTagMenu.Items.Add(tagItem);
        }

        return setTagMenu;
    }
}
