using ace_run.Models;
using ace_run.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ace_run;

public sealed partial class MainWindow : Window
{
    private readonly ObservableCollection<FolderViewModel> _folders = new();
    private readonly ObservableCollection<FolderViewModel> _sidebarItems = new();
    private readonly ObservableCollection<AppItemViewModel> _ungroupedApps = new();
    private readonly ObservableCollection<AppItemViewModel> _searchResults = new();
    private FolderViewModel? _selectedFolder; // null = ungrouped
    private FolderViewModel _ungroupedSentinel = null!;
    private AppData _appData = new();
    private string _searchText = string.Empty;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;

        _ungroupedSentinel = new FolderViewModel(Guid.Empty, Loc.GetString("UngroupedFolderName"));
        SidebarListView.ItemsSource = _sidebarItems;
        SearchResultsView.ItemsSource = _searchResults;

        LoadItems();
        RestoreWindowSize();
        Closed += MainWindow_Closed;
    }

    #region Data Load/Save

    private void LoadItems()
    {
        _appData = DataService.Load();
        _folders.Clear();
        _sidebarItems.Clear();
        _ungroupedApps.Clear();

        _sidebarItems.Add(_ungroupedSentinel);

        foreach (var app in _appData.UngroupedItems)
        {
            var vm = new AppItemViewModel(app);
            _ungroupedApps.Add(vm);
            _ = vm.LoadIconAsync();
        }

        foreach (var folder in _appData.Folders)
        {
            var fvm = new FolderViewModel(folder);
            foreach (var app in folder.Children)
            {
                var vm = new AppItemViewModel(app);
                fvm.Apps.Add(vm);
                _ = vm.LoadIconAsync();
            }
            _folders.Add(fvm);
            _sidebarItems.Add(fvm);
        }

        _selectedFolder = null;
        SidebarListView.SelectedItem = _ungroupedSentinel;
        RefreshContentArea();

        if (PurgeStaleRecentLaunches())
            DataService.Save(_appData);
    }

    private void RefreshContentArea()
    {
        AppGridView.ItemsSource = _selectedFolder?.Apps ?? _ungroupedApps;
    }

    private void CommitSave()
    {
        _appData.UngroupedItems = _ungroupedApps.Select(v => v.ToModel()).ToList();
        _appData.Folders = _folders.Select(f => f.ToModel()).ToList();
        DataService.Save(_appData);
    }

    private void SaveItems()
    {
        if (!string.IsNullOrEmpty(_searchText))
            return;
        CommitSave();
    }

    private bool PurgeStaleRecentLaunches()
    {
        var allIds = new HashSet<Guid>();
        foreach (var app in _ungroupedApps)
            allIds.Add(app.Id);
        foreach (var folder in _folders)
            foreach (var app in folder.Apps)
                allIds.Add(app.Id);

        int before = _appData.RecentLaunches.Count;
        _appData.RecentLaunches.RemoveAll(r => !allIds.Contains(r.AppId));
        return _appData.RecentLaunches.Count < before;
    }

    private AppItemViewModel? FindAppById(Guid id)
    {
        foreach (var app in _ungroupedApps)
            if (app.Id == id) return app;
        foreach (var folder in _folders)
            foreach (var app in folder.Apps)
                if (app.Id == id) return app;
        return null;
    }

    private async Task DeleteAppsAsync(IList<AppItemViewModel> targets)
    {
        if (targets.Count == 0) return;

        ContentDialog dialog;
        if (targets.Count == 1)
        {
            dialog = new ContentDialog
            {
                Title = Loc.GetString("DeleteItemTitle"),
                Content = string.Format(Loc.GetString("DeleteItemContent"), targets[0].DisplayName),
                PrimaryButtonText = Loc.GetString("DeleteButton"),
                CloseButtonText = Loc.GetString("CancelButton"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };
        }
        else
        {
            dialog = new ContentDialog
            {
                Title = Loc.GetString("DeleteSelectedTitle"),
                Content = string.Format(Loc.GetString("DeleteSelectedContent"), targets.Count),
                PrimaryButtonText = Loc.GetString("DeleteButton"),
                CloseButtonText = Loc.GetString("CancelButton"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };
        }

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        foreach (var app in targets)
        {
            _ungroupedApps.Remove(app);
            foreach (var folder in _folders)
                folder.Apps.Remove(app);
            _searchResults.Remove(app);
        }

        PurgeStaleRecentLaunches();
        CommitSave();
        ((App)Application.Current).UpdateTrayContextMenu();
    }

    private async Task DeleteFolderAsync(FolderViewModel folder)
    {
        var dialog = new ContentDialog
        {
            Title = Loc.GetString("DeleteFolder"),
            Content = string.Format(Loc.GetString("DeleteFolderContent"), folder.DisplayName),
            PrimaryButtonText = Loc.GetString("DeleteButton"),
            CloseButtonText = Loc.GetString("CancelButton"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        foreach (var app in folder.Apps)
            _searchResults.Remove(app);

        _folders.Remove(folder);
        _sidebarItems.Remove(folder);

        if (_selectedFolder == folder)
        {
            _selectedFolder = null;
            SidebarListView.SelectedItem = _ungroupedSentinel;
            RefreshContentArea();
        }

        PurgeStaleRecentLaunches();
        CommitSave();
        ((App)Application.Current).UpdateTrayContextMenu();
    }

    #endregion

    #region Sidebar

    private void SidebarListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SidebarListView.SelectedItem is FolderViewModel folder)
        {
            _selectedFolder = folder == _ungroupedSentinel ? null : folder;
            RefreshContentArea();
        }
    }

    private void SidebarListView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.Contains(_ungroupedSentinel))
            e.Cancel = true;
    }

    private void SidebarListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        // Keep sentinel pinned at index 0
        var idx = _sidebarItems.IndexOf(_ungroupedSentinel);
        if (idx != 0)
            _sidebarItems.Move(idx, 0);

        // Rebuild _folders to match the new order (sentinel excluded)
        _folders.Clear();
        foreach (var item in _sidebarItems)
            if (item != _ungroupedSentinel)
                _folders.Add(item);

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
            foreach (var app in _ungroupedApps)
                if (app.DisplayName.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
                    _searchResults.Add(app);
            foreach (var folder in _folders)
                foreach (var app in folder.Apps)
                    if (app.DisplayName.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
                        _searchResults.Add(app);
        }
    }

    #endregion

    #region Add / Edit / Delete / Move

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add(".exe");

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

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
            var target = _selectedFolder?.Apps ?? _ungroupedApps;
            target.Add(vm);
            _ = vm.LoadIconAsync();
            SaveItems();
        }
    }

    private void AddItemDirectly(string filePath)
    {
        var item = new AppItem
        {
            DisplayName = Path.GetFileNameWithoutExtension(filePath),
            FilePath = filePath,
            WorkingDirectory = Path.GetDirectoryName(filePath) ?? string.Empty
        };

        var vm = new AppItemViewModel(item);
        var target = _selectedFolder?.Apps ?? _ungroupedApps;
        target.Add(vm);
        _ = vm.LoadIconAsync();
        SaveItems();
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
            var name = string.IsNullOrWhiteSpace(nameBox.Text)
                ? Loc.GetString("DefaultFolderName")
                : nameBox.Text.Trim();
            var vm = new FolderViewModel(name);
            _folders.Add(vm);
            _sidebarItems.Add(vm);
            SaveItems();
        }
    }

    private void MoveAppTo(AppItemViewModel app, FolderViewModel? targetFolder)
    {
        _ungroupedApps.Remove(app);
        foreach (var folder in _folders)
            folder.Apps.Remove(app);

        var target = targetFolder?.Apps ?? _ungroupedApps;
        target.Add(app);

        CommitSave();
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

        if (tappedApp is null && selectedApps.Count == 0) return;

        if (tappedApp is not null && !selectedApps.Contains(tappedApp))
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

            // Move To submenu
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
        if (folder is null || folder == _ungroupedSentinel) return;

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

    private async Task RenameFolderAsync(FolderViewModel folder)
    {
        var nameBox = new TextBox { Text = folder.DisplayName };

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
                folder.DisplayName = nameBox.Text.Trim();
                SaveItems();
            }
        }
    }

    private async void EditApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: AppItemViewModel vm })
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var dialog = new EditItemDialog(vm, hwnd);
            dialog.XamlRoot = Content.XamlRoot;
            dialog.Title = Loc.GetString("EditItemTitle");

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                dialog.ApplyTo(vm);
                _ = vm.LoadIconAsync();
                SaveItems();
            }
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: AppItemViewModel vm })
        {
            var dir = Path.GetDirectoryName(vm.FilePath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{vm.FilePath}\"",
                    UseShellExecute = true
                });
            }
        }
    }

    #endregion

    #region Launch

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

    #endregion

    #region Search Results

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

    private async void SearchResultsView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Delete)
        {
            e.Handled = true;
            var targets = SearchResultsView.SelectedItems.Cast<AppItemViewModel>().ToList();
            await DeleteAppsAsync(targets);
        }
    }

    #endregion

    #region Window Lifecycle

    public void AttachContextMenus()
    {
        AppGridView.KeyDown += AppGridView_KeyDown;
        SidebarListView.RightTapped += SidebarListView_RightTapped;
    }

    private async void AppGridView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Delete)
        {
            e.Handled = true;
            var targets = AppGridView.SelectedItems.Cast<AppItemViewModel>().ToList();
            await DeleteAppsAsync(targets);
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        SaveWindowSize();

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
            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(ws.Width, ws.Height));
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

    #endregion

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(child);
        while (parent is not null)
        {
            if (parent is T t) return t;
            parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent);
        }
        return null;
    }
}
