using ace_run.Models;
using ace_run.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ace_run;

public sealed partial class MainWindow
{
    #region Add / Edit / Move

    /// <summary>Primary half of the Add split button — same as the "Add App" flyout entry.</summary>
    private async void AddSplitButton_Click(SplitButton sender, SplitButtonClickEventArgs args)
    {
        await PickAndAddAppAsync();
    }

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        await PickAndAddAppAsync();
    }

    // The modal guard spans the picker *and* the edit dialog that follows it, so a
    // keyboard accelerator can't slip a second dialog into the gap between the two.
    private Task PickAndAddAppAsync() => RunModalAsync(async () =>
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add(".exe");

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        await AddItemFromPathAsync(file.Path);
    });

    private async void AddUrlMenuItem_Click(object sender, RoutedEventArgs e)
    {
        // No separate "enter a URL" prompt — EditItemDialog switches to URL mode and the
        // user fills in address, name, tag and icon in one pass.
        await AddUrlAsync(string.Empty);
    }

    private static AppItem CreateAppItemFromPath(string filePath) => new()
    {
        DisplayName = Path.GetFileNameWithoutExtension(filePath),
        FilePath = filePath,
        WorkingDirectory = Path.GetDirectoryName(filePath) ?? string.Empty
    };

    // No WorkingDirectory: Path.GetDirectoryName on a URL yields junk like "https:\example.com".
    private static AppItem CreateUrlItem(string url) => new()
    {
        Kind = ItemKind.Url,
        DisplayName = url.Length > 0 ? UrlUtil.SuggestDisplayName(url) : string.Empty,
        FilePath = url
    };

    private Task AddItemFromPathAsync(string filePath) =>
        AddItemWithDialogAsync(CreateAppItemFromPath(filePath), "AddItemTitle");

    private Task AddUrlAsync(string url) =>
        AddItemWithDialogAsync(CreateUrlItem(url), "AddUrlTitle");

    private async Task AddItemWithDialogAsync(AppItem item, string titleKey)
    {
        var vm = new AppItemViewModel(item, _tags);
        var hwnd = WindowNative.GetWindowHandle(this);
        var dialog = new EditItemDialog(vm, hwnd, _tags);
        dialog.XamlRoot = Content.XamlRoot;
        dialog.Title = Loc.GetString(titleKey);

        if (await ShowModalAsync(dialog) == ContentDialogResult.Primary)
        {
            dialog.ApplyTo(vm);
            var target = _selectedFolder?.Apps ?? _ungroupedApps;
            target.Add(vm);
            _ = vm.LoadIconAsync();
            SaveItems();
        }
    }

    private void AddItemDirectly(string filePath, int? index = null) =>
        AddDirectly(CreateAppItemFromPath(filePath), index);

    private void AddUrlDirectly(string url, int? index = null) =>
        AddDirectly(CreateUrlItem(url), index);

    /// <param name="index">
    /// Where to insert, for a drop that landed between two tiles — see
    /// <c>ResolveGridDropIndex</c>. Null appends, which is what every other caller wants.
    /// Clamped rather than trusted: the collection can have moved on between the drop and
    /// the file read that follows it.
    /// </param>
    private void AddDirectly(AppItem item, int? index = null)
    {
        var vm = new AppItemViewModel(item, _tags);
        var target = _selectedFolder?.Apps ?? _ungroupedApps;

        if (index is int at)
            target.Insert(Math.Clamp(at, 0, target.Count), vm);
        else
            target.Add(vm);

        _ = vm.LoadIconAsync();
        SaveItems();
    }

    private async void NewFolderButton_Click(object sender, RoutedEventArgs e)
    {
        await NewFolderAsync();
    }

    private async Task NewFolderAsync()
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

        if (await ShowModalAsync(dialog) == ContentDialogResult.Primary)
        {
            var name = string.IsNullOrWhiteSpace(nameBox.Text)
                ? Loc.GetString("DefaultFolderName")
                : nameBox.Text.Trim();
            var vm = new FolderViewModel(name);
            _folders.Add(vm);
            SaveItems();

            // Invoked from the keyboard the rail may be collapsed, which would put the
            // new folder somewhere the user can't see it.
            RailSplitView.IsPaneOpen = true;
        }
    }

    /// <summary>
    /// Moves a batch of items into <paramref name="targetFolder"/> (null = ungrouped),
    /// appending them in the order given and saving once at the end.
    ///
    /// Callers pass the items in *visual* order, not selection order — see
    /// <see cref="SelectedAppsInOrder"/>. Landing order in the target is otherwise decided
    /// by the sequence the user happened to Ctrl+click in.
    /// </summary>
    private void MoveAppsTo(IList<AppItemViewModel> apps, FolderViewModel? targetFolder)
    {
        if (apps.Count == 0) return;

        var target = targetFolder?.Apps ?? _ungroupedApps;

        foreach (var app in apps)
        {
            _ungroupedApps.Remove(app);
            foreach (var folder in _folders)
                folder.Apps.Remove(app);

            target.Add(app);
        }

        CommitSave();
    }

    private async void EditApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: AppItemViewModel vm })
            await EditAppAsync(vm);
    }

    private async Task EditAppAsync(AppItemViewModel vm)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var dialog = new EditItemDialog(vm, hwnd, _tags);
        dialog.XamlRoot = Content.XamlRoot;
        dialog.Title = Loc.GetString(vm.IsUrl ? "EditUrlTitle" : "EditItemTitle");

        if (await ShowModalAsync(dialog) == ContentDialogResult.Primary)
        {
            dialog.ApplyTo(vm);
            _ = vm.LoadIconAsync();
            SaveItems();
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

    private void CopyUrl_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: AppItemViewModel vm })
        {
            var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            package.SetText(vm.FilePath);
            Clipboard.SetContent(package);
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

        if (await ShowModalAsync(dialog) == ContentDialogResult.Primary)
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
        LaunchCore(app);
        AfterLaunch();
    }

    /// <summary>
    /// Launches a batch. The per-item work is identical to <see cref="LaunchApp"/>; only the
    /// save and the tray rebuild are hoisted out of the loop — ten selected items used to
    /// mean ten workspace writes and ten tray menu rebuilds.
    ///
    /// Items are launched in the order given, so the last one ends up at the top of the
    /// recent list (<see cref="TrackRecentLaunch"/> inserts at 0).
    /// </summary>
    private void LaunchApps(IList<AppItemViewModel> apps)
    {
        if (apps.Count == 0) return;

        foreach (var app in apps)
            LaunchCore(app);

        AfterLaunch();
    }

    private void LaunchCore(AppItemViewModel app)
    {
        // Before the try: the confirmation is for the click, not for the outcome.
        PulseLaunch(app);

        try
        {
            // ShellExecute handles http(s) and custom protocols as-is. Arguments would go to
            // the protocol handler rather than the URL, and WorkingDirectory / runas are
            // meaningless for a URL, so they are only set for App items.
            var psi = new ProcessStartInfo
            {
                FileName = app.FilePath,
                UseShellExecute = true
            };
            if (!app.IsUrl)
            {
                psi.Arguments = app.Arguments;
                psi.WorkingDirectory = app.WorkingDirectory;
                if (app.RunAsAdmin)
                    psi.Verb = "runas";
            }

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
