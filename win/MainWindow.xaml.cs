using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
using ace_run.Models;
using ace_run.Services;

namespace ace_run;

public sealed partial class MainWindow : Window
{
    private readonly List<TreeItemViewModel> _rootItems = new();
    private readonly ObservableCollection<AppItemViewModel> _searchResults = new();
    private AppData _appData = new();
    private string _searchText = string.Empty;

    // Drag state
    private TreeItemViewModel? _draggedItem;

    public MainWindow()
    {
        InitializeComponent();

        SearchResultsView.ItemsSource = _searchResults;
        LoadItems();
        RestoreWindowSize();
        Closed += MainWindow_Closed;
    }

    #region Data Load/Save

    private void LoadItems()
    {
        _appData = DataService.Load();
        _rootItems.Clear();
        AppTreeView.RootNodes.Clear();

        foreach (var item in _appData.Items)
            AddTreeItem(item, null);
    }

    private void AddTreeItem(TreeItem model, TreeViewNode? parentNode)
    {
        if (model is AppItem appItem)
        {
            var vm = new AppItemViewModel(appItem);
            var node = new TreeViewNode { Content = vm, IsExpanded = false };
            if (parentNode is not null)
                parentNode.Children.Add(node);
            else
                AppTreeView.RootNodes.Add(node);

            AddToRootItems(vm, parentNode);
            _ = vm.LoadIconAsync();
        }
        else if (model is FolderItem folderItem)
        {
            var vm = new FolderViewModel(folderItem);
            var node = new TreeViewNode { Content = vm, IsExpanded = folderItem.IsExpanded, HasUnrealizedChildren = false };
            if (parentNode is not null)
                parentNode.Children.Add(node);
            else
                AppTreeView.RootNodes.Add(node);

            AddToRootItems(vm, parentNode);

            foreach (var child in folderItem.Children)
                AddTreeItem(child, node);
        }
    }

    private void AddToRootItems(TreeItemViewModel vm, TreeViewNode? parentNode)
    {
        if (parentNode is null)
        {
            _rootItems.Add(vm);
        }
        else if (parentNode.Content is FolderViewModel folder)
        {
            folder.Children.Add(vm);
        }
    }

    private void SaveItems()
    {
        if (!string.IsNullOrEmpty(_searchText))
            return; // Don't save while filtering

        // Sync expansion state from tree nodes
        SyncExpansionState(AppTreeView.RootNodes);

        _appData.Items = BuildModelList(_rootItems);
        DataService.Save(_appData);
    }

    private void SyncExpansionState(IList<TreeViewNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Content is FolderViewModel folder)
            {
                folder.IsExpanded = node.IsExpanded;
                SyncExpansionState(node.Children);
            }
        }
    }

    private List<TreeItem> BuildModelList(IEnumerable<TreeItemViewModel> items)
    {
        var list = new List<TreeItem>();
        foreach (var vm in items)
            list.Add(vm.ToModel());
        return list;
    }

    #endregion

    #region Tree Context Menus

    private void AppTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        // Right-click is handled via context menu attached in code-behind
    }

    public void ShowContextMenuForNode(TreeViewNode node, FrameworkElement anchor)
    {
        var flyout = new MenuFlyout();

        if (node.Content is AppItemViewModel appVm)
        {
            var editItem = new MenuFlyoutItem
            {
                Text = Loc.GetString("EditMenuItem.Text"),
                Icon = new FontIcon { Glyph = "\uE70F" },
                Tag = appVm
            };
            editItem.Click += EditTreeItem_Click;
            flyout.Items.Add(editItem);

            var deleteItem = new MenuFlyoutItem
            {
                Text = Loc.GetString("DeleteMenuItem.Text"),
                Icon = new FontIcon { Glyph = "\uE74D" },
                Tag = appVm
            };
            deleteItem.Click += DeleteTreeItem_Click;
            flyout.Items.Add(deleteItem);
        }
        else if (node.Content is FolderViewModel folderVm)
        {
            var renameItem = new MenuFlyoutItem
            {
                Text = Loc.GetString("RenameFolder"),
                Icon = new FontIcon { Glyph = "\uE8AC" },
                Tag = folderVm
            };
            renameItem.Click += RenameFolder_Click;
            flyout.Items.Add(renameItem);

            var deleteItem = new MenuFlyoutItem
            {
                Text = Loc.GetString("DeleteFolder"),
                Icon = new FontIcon { Glyph = "\uE74D" },
                Tag = folderVm
            };
            deleteItem.Click += DeleteFolder_Click;
            flyout.Items.Add(deleteItem);
        }

        flyout.ShowAt(anchor);
    }

    #endregion

    #region Search

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        _searchText = sender.Text;

        if (string.IsNullOrEmpty(_searchText))
        {
            // Restore tree view
            SearchResultsView.Visibility = Visibility.Collapsed;
            AppTreeView.Visibility = Visibility.Visible;
            _searchResults.Clear();
        }
        else
        {
            // Flatten tree and filter
            AppTreeView.Visibility = Visibility.Collapsed;
            SearchResultsView.Visibility = Visibility.Visible;

            _searchResults.Clear();
            CollectMatchingApps(_rootItems, _searchText);
        }
    }

    private void CollectMatchingApps(IEnumerable<TreeItemViewModel> items, string query)
    {
        foreach (var item in items)
        {
            if (item is AppItemViewModel app &&
                app.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                _searchResults.Add(app);
            }
            else if (item is FolderViewModel folder)
            {
                CollectMatchingApps(folder.Children, query);
            }
        }
    }

    #endregion

    #region Add / Edit / Delete

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = WindowNative.GetWindowHandle(this);

        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add(".exe");

        var file = await picker.PickSingleFileAsync();
        if (file is null)
            return;

        await AddItemFromPathAsync(file.Path);
    }

    private async Task AddItemFromPathAsync(string filePath)
    {
        var item = new AppItem
        {
            DisplayName = Path.GetFileNameWithoutExtension(filePath),
            FilePath = filePath,
            WorkingDirectory = Path.GetDirectoryName(filePath) ?? string.Empty
        };

        var vm = new AppItemViewModel(item);
        var hwnd = WindowNative.GetWindowHandle(this);
        var dialog = new EditItemDialog(vm, hwnd);
        dialog.XamlRoot = Content.XamlRoot;
        dialog.Title = Loc.GetString("AddItemTitle");

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            dialog.ApplyTo(vm);
            _rootItems.Add(vm);
            var node = new TreeViewNode { Content = vm };
            AppTreeView.RootNodes.Add(node);
            _ = vm.LoadIconAsync();
            SaveItems();
        }
    }

    private async void NewFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox
        {
            PlaceholderText = Loc.GetString("FolderNamePlaceholder")
        };

        var dialog = new ContentDialog
        {
            Title = Loc.GetString("NewFolderTitle"),
            Content = nameBox,
            PrimaryButtonText = Loc.GetString("SaveButton"),
            CloseButtonText = Loc.GetString("CancelButton"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var name = string.IsNullOrWhiteSpace(nameBox.Text) ? Loc.GetString("DefaultFolderName") : nameBox.Text.Trim();
            var vm = new FolderViewModel(name);
            _rootItems.Add(vm);
            var node = new TreeViewNode { Content = vm, IsExpanded = true };
            AppTreeView.RootNodes.Add(node);
            SaveItems();
        }
    }

    private async void EditTreeItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: AppItemViewModel vm })
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var dialog = new EditItemDialog(vm, hwnd);
            dialog.XamlRoot = Content.XamlRoot;

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                dialog.ApplyTo(vm);
                _ = vm.LoadIconAsync();
                SaveItems();
            }
        }
    }

    private async void DeleteTreeItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: AppItemViewModel vm })
        {
            var confirmDialog = new ContentDialog
            {
                Title = Loc.GetString("DeleteItemTitle"),
                Content = string.Format(Loc.GetString("DeleteItemContent"), vm.DisplayName),
                PrimaryButtonText = Loc.GetString("DeleteButton"),
                CloseButtonText = Loc.GetString("CancelButton"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };

            if (await confirmDialog.ShowAsync() == ContentDialogResult.Primary)
            {
                RemoveViewModel(vm, _rootItems, AppTreeView.RootNodes);
                SaveItems();
            }
        }
    }

    private async void RenameFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: FolderViewModel vm })
        {
            var nameBox = new TextBox { Text = vm.DisplayName };

            var dialog = new ContentDialog
            {
                Title = Loc.GetString("RenameFolder"),
                Content = nameBox,
                PrimaryButtonText = Loc.GetString("SaveButton"),
                CloseButtonText = Loc.GetString("CancelButton"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                if (!string.IsNullOrWhiteSpace(nameBox.Text))
                {
                    vm.DisplayName = nameBox.Text.Trim();
                    // Refresh the node display
                    RefreshNodeForViewModel(vm, AppTreeView.RootNodes);
                    SaveItems();
                }
            }
        }
    }

    private async void DeleteFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: FolderViewModel vm })
        {
            var confirmDialog = new ContentDialog
            {
                Title = Loc.GetString("DeleteFolder"),
                Content = string.Format(Loc.GetString("DeleteFolderContent"), vm.DisplayName),
                PrimaryButtonText = Loc.GetString("DeleteButton"),
                CloseButtonText = Loc.GetString("CancelButton"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };

            if (await confirmDialog.ShowAsync() == ContentDialogResult.Primary)
            {
                RemoveViewModel(vm, _rootItems, AppTreeView.RootNodes);
                SaveItems();
            }
        }
    }

    private bool RemoveViewModel(TreeItemViewModel target, IList<TreeItemViewModel> items, IList<TreeViewNode> nodes)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] == target)
            {
                items.RemoveAt(i);
                nodes.RemoveAt(i);
                return true;
            }
            if (items[i] is FolderViewModel folder)
            {
                if (RemoveViewModel(target, folder.Children, nodes[i].Children))
                    return true;
            }
        }
        return false;
    }

    private void RefreshNodeForViewModel(TreeItemViewModel target, IList<TreeViewNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Content == target)
            {
                // Force re-bind by resetting content
                node.Content = null;
                node.Content = target;
                return;
            }
            RefreshNodeForViewModel(target, node.Children);
        }
    }

    #endregion

    #region Launch

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Guid id)
        {
            var app = FindAppById(id, _rootItems);
            if (app is not null)
                LaunchApp(app);
        }
    }

    public void LaunchApp(AppItemViewModel app)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = app.FilePath,
                Arguments = app.Arguments,
                WorkingDirectory = app.WorkingDirectory,
                UseShellExecute = true
            };
            if (app.RunAsAdmin)
                psi.Verb = "runas";

            Process.Start(psi);
            TrackRecentLaunch(app);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to launch: {ex.Message}");
        }
    }

    private void TrackRecentLaunch(AppItemViewModel app)
    {
        _appData.RecentLaunches.RemoveAll(r => r.AppId == app.Id);
        _appData.RecentLaunches.Insert(0, new RecentLaunch
        {
            AppId = app.Id,
            DisplayName = app.DisplayName,
            FilePath = app.FilePath
        });
        if (_appData.RecentLaunches.Count > 10)
            _appData.RecentLaunches.RemoveRange(10, _appData.RecentLaunches.Count - 10);
        SaveItems();
    }

    public List<RecentLaunch> GetRecentLaunches() => _appData.RecentLaunches;

    private AppItemViewModel? FindAppById(Guid id, IEnumerable<TreeItemViewModel> items)
    {
        foreach (var item in items)
        {
            if (item is AppItemViewModel app && app.Id == id)
                return app;
            if (item is FolderViewModel folder)
            {
                var found = FindAppById(id, folder.Children);
                if (found is not null)
                    return found;
            }
        }
        return null;
    }

    #endregion

    #region Drag & Drop (internal tree reorder)

    private void AppTreeView_DragItemsStarting(TreeView sender, TreeViewDragItemsStartingEventArgs args)
    {
        if (args.Items.Count > 0 && args.Items[0] is TreeViewNode node)
        {
            _draggedItem = node.Content as TreeItemViewModel;
        }
        else
        {
            _draggedItem = null;
        }
    }

    private void AppTreeView_DragItemsCompleted(TreeView sender, TreeViewDragItemsCompletedEventArgs args)
    {
        if (_draggedItem is null)
            return;

        // Rebuild _rootItems from tree nodes
        RebuildRootItemsFromNodes();
        _draggedItem = null;
        SaveItems();
    }

    private void RebuildRootItemsFromNodes()
    {
        _rootItems.Clear();
        foreach (var node in AppTreeView.RootNodes)
            RebuildFromNode(node, _rootItems);
    }

    private void RebuildFromNode(TreeViewNode node, IList<TreeItemViewModel> target)
    {
        if (node.Content is FolderViewModel folder)
        {
            folder.Children.Clear();
            target.Add(folder);
            foreach (var child in node.Children)
                RebuildFromNode(child, folder.Children);
        }
        else if (node.Content is TreeItemViewModel vm)
        {
            target.Add(vm);
        }
    }

    #endregion

    #region Drag & Drop (external files)

    private void RootGrid_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = Loc.GetString("DragDropCaption");
            e.DragUIOverride.IsGlyphVisible = true;
        }
        else if (_draggedItem is not null)
        {
            // Internal tree drag - allow move
            e.AcceptedOperation = DataPackageOperation.Move;
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
    }

    private async void RootGrid_Drop(object sender, DragEventArgs e)
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
                {
                    filePath = ResolveLnkTarget(storageItem.Path) ?? storageItem.Path;
                }

                if (filePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(filePath))
                {
                    await AddItemFromPathAsync(filePath);
                }
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

    #endregion

    #region Right-click on tree items

    // Called from code to attach context flyout to tree view items
    public void AttachContextMenus()
    {
        AppTreeView.RightTapped += AppTreeView_RightTapped;
    }

    private void AppTreeView_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement fe)
        {
            // Walk up the visual tree to find the TreeViewItem
            var tvi = FindParent<TreeViewItem>(fe);
            if (tvi is not null)
            {
                var node = AppTreeView.NodeFromContainer(tvi);
                if (node is not null)
                {
                    ShowContextMenuForNode(node, tvi);
                    e.Handled = true;
                }
            }
        }
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(child);
        while (parent is not null)
        {
            if (parent is T t)
                return t;
            parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    #endregion

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        SaveWindowSize();

        // If tray is enabled, hide instead of closing
        if (App.TrayEnabled)
        {
            args.Handled = true;
            this.AppWindow.Hide();
            return;
        }
        SaveItems();
    }

    private void RestoreWindowSize()
    {
        var ws = _appData.WindowState;
        if (ws is not null && ws.Width > 0 && ws.Height > 0)
        {
            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(ws.Width, ws.Height));
        }
    }

    private void SaveWindowSize()
    {
        var size = this.AppWindow.Size;
        _appData.WindowState = new Models.WindowState
        {
            Width = size.Width,
            Height = size.Height
        };
        DataService.Save(_appData);
    }
}
