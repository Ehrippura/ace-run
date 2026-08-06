using ace_run.Models;
using ace_run.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ace_run;

public sealed partial class MainWindow
{
    #region Workspace Initialization

    private async Task InitializeWorkspacesAsync()
    {
        _workspaceConfig = DataService.MigrateOrInitialize();

        _workspaces.Clear();
        foreach (var ws in _workspaceConfig.Workspaces)
            _workspaces.Add(new WorkspaceViewModel(ws));

        var active = _workspaceConfig.Workspaces.FirstOrDefault(w => w.Id == _workspaceConfig.ActiveWorkspaceId)
                     ?? _workspaceConfig.Workspaces.First();
        _currentWorkspace = active;

        _suppressWorkspaceSwitch = true;
        WorkspaceComboBox.SelectedItem = _workspaces.FirstOrDefault(v => v.Id == active.Id);
        _suppressWorkspaceSwitch = false;

        await LoadWorkspaceDataAsync(active);
        ApplyWorkspaceIdentity();
    }

    private async Task LoadWorkspaceDataAsync(WorkspaceInfo ws)
    {
        _appData = DataService.LoadWorkspace(ws.Id);
        _folders.Clear();
        _ungroupedApps.Clear();
        _tags.Clear();

        _appData.Tags ??= new System.Collections.Generic.List<TagItem>();
        foreach (var tag in _appData.Tags)
            _tags.Add(new TagViewModel(tag));

        foreach (var app in _appData.UngroupedItems)
        {
            var vm = new AppItemViewModel(app, _tags);
            _ungroupedApps.Add(vm);
        }

        foreach (var folder in _appData.Folders)
        {
            var fvm = new FolderViewModel(folder);
            foreach (var app in folder.Children)
            {
                var vm = new AppItemViewModel(app, _tags);
                fvm.Apps.Add(vm);
            }
            _folders.Add(fvm);
        }

        var savedFolder = ws.SelectedFolderId is Guid fid
            ? _folders.FirstOrDefault(f => f.Id == fid)
            : null;

        NavigateToFolder(savedFolder);

        if (PurgeStaleRecentLaunches())
            DataService.SaveWorkspace(ws.Id, _appData);
    }

    private void ResetContentState()
    {
        ExitSearchMode();

        _ungroupedApps.Clear();
        _folders.Clear();
        _tags.Clear();
        _selectedFolder = null;
    }

    private async Task SwitchWorkspaceAsync(WorkspaceInfo target)
    {
        if (target.Id == _currentWorkspace.Id) return;

        CommitSave();
        ResetContentState();

        _currentWorkspace = target;
        _workspaceConfig.ActiveWorkspaceId = target.Id;
        DataService.SaveConfig(_workspaceConfig);

        await LoadWorkspaceDataAsync(target);
        ApplyWorkspaceIdentity();
        FadeInContent();
        ((App)Application.Current).UpdateTrayContextMenu();
    }

    public async Task ReloadAfterWorkspaceManagement()
    {
        _workspaceConfig = DataService.LoadConfig();

        _suppressWorkspaceSwitch = true;
        _workspaces.Clear();
        foreach (var ws in _workspaceConfig.Workspaces)
            _workspaces.Add(new WorkspaceViewModel(ws));

        var current = _workspaceConfig.Workspaces.FirstOrDefault(w => w.Id == _currentWorkspace.Id)
                      ?? _workspaceConfig.Workspaces.First();
        _currentWorkspace = current;

        WorkspaceComboBox.SelectedItem = _workspaces.FirstOrDefault(v => v.Id == current.Id);
        _suppressWorkspaceSwitch = false;

        ResetContentState();

        await LoadWorkspaceDataAsync(current);
        ApplyWorkspaceIdentity();
        FadeInContent();
        ((App)Application.Current).UpdateTrayContextMenu();
    }

    /// <summary>
    /// Applies everything that identifies the active workspace: the taskbar/Alt-Tab title
    /// (never visible inside the app) and the spine, which is.
    /// </summary>
    private void ApplyWorkspaceIdentity()
    {
        AppWindow.Title = $"Ace Run \u2014 {_currentWorkspace.Name}";
        UpdateWorkspaceSpine();
    }

    public WorkspaceConfig WorkspaceConfig => _workspaceConfig;
    public WorkspaceInfo CurrentWorkspace => _currentWorkspace;

    #endregion

    #region Workspace ComboBox / Manage Button

    private async void WorkspaceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressWorkspaceSwitch) return;
        if (WorkspaceComboBox.SelectedItem is WorkspaceViewModel wsVm)
            await SwitchWorkspaceAsync(wsVm.ToInfo());
    }

    // The modal guard covers the reload as well as the dialog: _suppressWorkspaceSwitch is
    // true for part of ReloadAfterWorkspaceManagement, and a Ctrl+N landing in that window
    // would move the picker without switching the data behind it.
    private async void ManageWorkspacesButton_Click(object sender, RoutedEventArgs e) =>
        await RunModalAsync(async () =>
        {
            // Persist the current folder selection before the dialog opens. Sidebar clicks
            // don't save SelectedFolderId on their own, so without this the dialog would load
            // (and later restore) a stale folder, snapping the sidebar back after confirming.
            CommitSave();

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var dialog = new ManageWorkspacesDialog(hwnd, _currentWorkspace.Id);
            dialog.XamlRoot = Content.XamlRoot;
            await dialog.ShowAsync();
            await ReloadAfterWorkspaceManagement();
        });

    private async void ManageTagsButton_Click(object sender, RoutedEventArgs e) =>
        await RunModalAsync(async () =>
        {
            var dialog = new ManageTagsDialog(_tags);
            dialog.XamlRoot = Content.XamlRoot;
            await dialog.ShowAsync();

            // Reconcile apps with the (possibly) changed tag list, then persist. Renames
            // and recolors need no pass here: the tiles bind the tag instances directly.
            NormalizeAppTags();
            CommitSave();
        });

    #endregion
}
