using ace_run.Models;
using ace_run.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ace_run;

public sealed partial class MainWindow
{
    #region Add / Edit / Move

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
        ((App)Application.Current).UpdateTrayContextMenu();
    }

    public List<RecentLaunch> GetRecentLaunches() => _appData.RecentLaunches;

    public void ClearRecentLaunches()
    {
        _appData.RecentLaunches.Clear();
        SaveItems();
        ((App)Application.Current).UpdateTrayContextMenu();
    }

    #endregion
}
