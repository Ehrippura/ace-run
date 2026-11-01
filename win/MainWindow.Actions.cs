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

    /// <summary>Identifies this call site to the shell, which reopens where it last left off.</summary>
    /// <remarks>
    /// No start folder is passed — a new item has no path to open next to — so this key is
    /// the whole of it. Adding several apps in a row usually means several trips into the
    /// same Program Files or game library, which is what there is to remember here.
    /// </remarks>
    private const string AddAppSettingsId = "AceRun.AddApp.File";

    // The modal guard spans the picker *and* the edit dialog that follows it, so a
    // keyboard accelerator can't slip a second dialog into the gap between the two. It
    // also stands in for EditItemDialog's _pickerOpen: the SDK picker is asynchronous and
    // leaves the Add button live, and _modalDepth covers that gap as well.
    private Task PickAndAddAppAsync() => RunModalAsync(async () =>
    {
        var path = await FilePicker.PickFileAsync(AppWindow.Id, AddAppSettingsId, null, ".exe");
        if (path is null) return;

        await AddItemFromPathAsync(path);
    });

    private async void AddUrlMenuItem_Click(object sender, RoutedEventArgs e)
    {
        // No separate "enter a URL" prompt — EditItemDialog switches to URL mode and the
        // user fills in address, name, tag and icon in one pass.
        await AddUrlAsync(string.Empty);
    }

    private Task AddItemFromPathAsync(string filePath) =>
        AddItemWithDialogAsync(ItemFactory.FromPath(filePath), "AddItemTitle");

    private Task AddUrlAsync(string url) =>
        AddItemWithDialogAsync(ItemFactory.FromUrl(url), "AddUrlTitle");

    private async Task AddItemWithDialogAsync(AppItem item, string titleKey)
    {
        var vm = new AppItemViewModel(item, _tags);
        var dialog = new EditItemDialog(vm, AppWindow.Id, _tags);
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
        AddDirectly(ItemFactory.FromPath(filePath), index);

    private void AddUrlDirectly(string url, int? index = null) =>
        AddDirectly(ItemFactory.FromUrl(url), index);

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
            CreateFolderCore(string.IsNullOrWhiteSpace(nameBox.Text)
                ? Loc.GetString("DefaultFolderName")
                : nameBox.Text.Trim());

            // Invoked from the keyboard the rail may be collapsed, which would put the
            // new folder somewhere the user can't see it.
            RailSplitView.IsPaneOpen = true;
        }
    }

    /// <summary>
    /// Appends a folder and saves. Shared with the automation path, which supplies the name
    /// instead of asking for it.
    /// </summary>
    /// <remarks>
    /// <see cref="PersistAfterEdit"/> rather than the <c>SaveItems()</c> this used to call.
    /// That one returns early while the search box has text, so a folder created during a
    /// search was not written until something else saved — a latent bug found while splitting
    /// this out, and one the automation path would hit every time a user left a query up.
    /// </remarks>
    private FolderViewModel CreateFolderCore(string name)
    {
        var folder = new FolderViewModel(name);
        _folders.Add(folder);
        PersistAfterEdit();
        return folder;
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
        var dialog = new EditItemDialog(vm, AppWindow.Id, _tags);
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
                RenameFolderCore(folder, nameBox.Text.Trim());
        }
    }

    /// <inheritdoc cref="CreateFolderCore"/>
    private void RenameFolderCore(FolderViewModel folder, string name)
    {
        folder.DisplayName = name;
        PersistAfterEdit();
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

    /// <summary>
    /// Starts one item.
    /// </summary>
    /// <returns>
    /// What the OS threw, or null. The two UI callers ignore it — a failed launch is already
    /// reported by the program not appearing — but an automation caller gets no such feedback
    /// and has to be told (12-mcp-automation.md §B4).
    /// </returns>
    private Exception? LaunchCore(AppItemViewModel app)
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
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to launch: {ex.Message}");
            return ex;
        }
    }

    private void TrackRecentLaunch(AppItemViewModel app)
        => RecentLaunchList.Track(_appData.RecentLaunches, app);

    /// <summary>
    /// Launches a recent-list entry, given the id the tray menu was built from.
    /// </summary>
    /// <remarks>
    /// A <see cref="RecentLaunch"/> is an id and a label, nothing else, so this resolves the
    /// live item and hands it to the ordinary launch path. The tray used to start the path it
    /// had recorded, which silently dropped the arguments, the working directory and the admin
    /// verb — a program that needs its own working directory failed from the tray and worked
    /// from the window.
    ///
    /// A missing id is ignored rather than reported: <c>PurgeStaleRecentLaunches</c> runs
    /// whenever the workspace changes, so a miss means the item died between the menu being
    /// built and being clicked.
    /// </remarks>
    public void LaunchRecent(Guid appId)
    {
        if (FindAppById(appId) is { } app)
            LaunchApp(app);
    }

    public List<RecentLaunch> GetRecentLaunches() => _appData.RecentLaunches;

    /// <summary>
    /// Empties the launch history. Reachable from the tray and from automation, neither of
    /// which can see whether the user has left a query in the search box — hence
    /// <see cref="PersistAfterEdit"/> and not <c>SaveItems()</c>, which would have dropped the
    /// write in that state.
    /// </summary>
    /// <returns>How many entries went, so the caller can report it.</returns>
    public int ClearRecentLaunches()
    {
        var cleared = _appData.RecentLaunches.Count;

        _appData.RecentLaunches.Clear();
        PersistAfterEdit();
        ((App)Application.Current).UpdateTrayContextMenu();

        return cleared;
    }

    #endregion
}
