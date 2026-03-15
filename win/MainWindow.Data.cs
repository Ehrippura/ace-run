using ace_run.Models;
using ace_run.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ace_run;

public sealed partial class MainWindow
{
    #region Data Load/Save

    private void RefreshContentArea()
    {
        AppGridView.ItemsSource = _selectedFolder?.Apps ?? _ungroupedApps;
        ReleaseHiddenIcons();
    }

    private void ReleaseHiddenIcons()
    {
        var visible = _selectedFolder?.Apps ?? (IEnumerable<AppItemViewModel>)_ungroupedApps;

        if (visible != (IEnumerable<AppItemViewModel>)_ungroupedApps)
            foreach (var vm in _ungroupedApps) vm.ReleaseIcon();

        foreach (var folder in _folders)
            if (visible != (IEnumerable<AppItemViewModel>)folder.Apps)
                foreach (var vm in folder.Apps) vm.ReleaseIcon();
    }

    private void AppGridView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is not AppItemViewModel vm) return;
        if (args.InRecycleQueue)
            vm.ReleaseIcon();
        else
            _ = vm.LoadIconAsync();
    }

    private void CommitSave()
    {
        _appData.UngroupedItems = _ungroupedApps.Select(v => v.ToModel()).ToList();
        _appData.Folders = _folders.Select(f => f.ToModel()).ToList();

        var info = _workspaceConfig.Workspaces.FirstOrDefault(w => w.Id == _currentWorkspace.Id);
        if (info is not null)
        {
            info.AppCount = _appData.UngroupedItems.Count + _appData.Folders.Sum(f => f.Children.Count);
            info.LastModifiedAt = DateTime.UtcNow;
            info.SelectedFolderId = _selectedFolder?.Id;
            DataService.SaveConfig(_workspaceConfig);
        }

        DataService.SaveWorkspace(_currentWorkspace.Id, _appData);
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

    #endregion

    #region Delete

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

        if (_selectedFolder == folder)
        {
            _selectedFolder = null;
            SidebarListView.SelectedItem = null;
            UngroupedItem.IsSelected = true;
            RefreshContentArea();
        }

        PurgeStaleRecentLaunches();
        CommitSave();
        ((App)Application.Current).UpdateTrayContextMenu();
    }

    #endregion
}
