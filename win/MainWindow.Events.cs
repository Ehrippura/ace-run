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

    private void SidebarListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SidebarListView.SelectedItem is FolderViewModel folder)
        {
            UngroupedItem.IsSelected = false;
            _selectedFolder = folder;
            RefreshContentArea();
        }
    }

    private void UngroupedItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        SidebarListView.SelectedItem = null;
        _selectedFolder = null;
        UngroupedItem.IsSelected = true;
        RefreshContentArea();
    }

    private void SidebarListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        CommitSave();
    }

    #endregion

    #region Search

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        _searchText = sender.Text;

        if (string.IsNullOrEmpty(_searchText))
        {
            SearchResultsView.Visibility = Visibility.Collapsed;
            AppGridView.Visibility = Visibility.Visible;
            _searchResults.Clear();
        }
        else
        {
            AppGridView.Visibility = Visibility.Collapsed;
            SearchResultsView.Visibility = Visibility.Visible;

            _searchResults.Clear();
            var ungroupedLabel = Loc.GetString("UngroupedFolderName");
            foreach (var app in _ungroupedApps)
                if (app.DisplayName.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
                {
                    app.FolderLabel = ungroupedLabel;
                    _searchResults.Add(app);
                }
            foreach (var folder in _folders)
                foreach (var app in folder.Apps)
                    if (app.DisplayName.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
                    {
                        app.FolderLabel = folder.DisplayName;
                        _searchResults.Add(app);
                    }

            foreach (var app in _searchResults)
                _ = app.LoadIconAsync();
        }
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

        flyout.ShowAt(fe, new FlyoutShowOptions { Position = e.GetPosition(fe) });
        e.Handled = true;
    }

    /// <summary>Clears the search and switches the content area to the folder that
    /// contains <paramref name="app"/> (or the ungrouped page), then selects it.</summary>
    private void NavigateToAppFolder(AppItemViewModel app)
    {
        var folder = FindFolderOfApp(app);

        // Resets to normal view (collapses search results, clears _searchResults).
        SearchBox.Text = string.Empty;

        if (folder is not null)
        {
            UngroupedItem.IsSelected = false;
            SidebarListView.SelectedItem = folder; // triggers SelectionChanged -> RefreshContentArea
        }
        else
        {
            SidebarListView.SelectedItem = null;
            _selectedFolder = null;
            UngroupedItem.IsSelected = true;
            RefreshContentArea();
        }

        AppGridView.SelectedItem = app;
        AppGridView.ScrollIntoView(app);
    }

    private async void SearchResultsView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            if (SearchResultsView.SelectedItem is AppItemViewModel app)
                LaunchApp(app);
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
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
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
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            return;

        var deferral = e.GetDeferral();
        try
        {
            var storageItems = await e.DataView.GetStorageItemsAsync();
            foreach (var storageItem in storageItems.OfType<StorageFile>())
            {
                var filePath = storageItem.Path;

                if (storageItem.FileType.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                    filePath = ResolveLnkTarget(storageItem.Path) ?? storageItem.Path;

                if (filePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(filePath))
                    AddItemDirectly(filePath);
            }
        }
        finally
        {
            deferral.Complete();
        }
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
                LaunchApp(app);
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

            var editItem = new MenuFlyoutItem
            {
                Text = Loc.GetString("EditMenuItem.Text"),
                Icon = new FontIcon { Glyph = "\uE70F" },
                Tag = app
            };
            editItem.Click += EditApp_Click;
            flyout.Items.Add(editItem);

            var openFolderItem = new MenuFlyoutItem
            {
                Text = Loc.GetString("OpenFolderMenuItem.Text"),
                Icon = new FontIcon { Glyph = "\uE838" },
                Tag = app
            };
            openFolderItem.Click += OpenFolder_Click;
            flyout.Items.Add(openFolderItem);

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

            var currentTagId = app.TagIds.Count > 0 ? app.TagIds[0] : (Guid?)null;

            var noTagItem = new RadioMenuFlyoutItem
            {
                Text = Loc.GetString("Tag_None"),
                GroupName = "AppTag",
                IsChecked = currentTagId is null
            };
            noTagItem.Click += (_, _) => ApplyTagToApp(app, null);
            setTagMenu.Items.Add(noTagItem);

            foreach (var tag in _tags)
            {
                var tagCapture = tag;
                var tagItem = new RadioMenuFlyoutItem
                {
                    Text = tag.Name,
                    GroupName = "AppTag",
                    IsChecked = currentTagId is Guid cid && cid == tag.Id,
                    Icon = new FontIcon { Glyph = "\uEA3B", Foreground = tag.ColorBrush }
                };
                tagItem.Click += (_, _) => ApplyTagToApp(app, tagCapture.Id);
                setTagMenu.Items.Add(tagItem);
            }

            flyout.Items.Add(setTagMenu);

            flyout.Items.Add(new MenuFlyoutSeparator());

            var deleteItem = new MenuFlyoutItem
            {
                Text = Loc.GetString("DeleteMenuItem.Text"),
                Icon = new FontIcon { Glyph = "\uE74D" }
            };
            deleteItem.Click += async (_, _) => await DeleteAppsAsync(new[] { app });
            flyout.Items.Add(deleteItem);
        }

        flyout.ShowAt(fe, new FlyoutShowOptions { Position = e.GetPosition(fe) });
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
            Icon = new FontIcon { Glyph = "\uE8AC" }
        };
        renameItem.Click += async (_, _) => await RenameFolderAsync(folder);
        flyout.Items.Add(renameItem);

        flyout.Items.Add(new MenuFlyoutSeparator());

        var deleteItem = new MenuFlyoutItem
        {
            Text = Loc.GetString("DeleteFolder"),
            Icon = new FontIcon { Glyph = "\uE74D" }
        };
        deleteItem.Click += async (_, _) => await DeleteFolderAsync(folder);
        flyout.Items.Add(deleteItem);

        flyout.ShowAt(fe, new FlyoutShowOptions { Position = e.GetPosition(fe) });
        e.Handled = true;
    }

    #endregion
}
