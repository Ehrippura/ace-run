using ace_run.Models;
using ace_run.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;

namespace ace_run;

public sealed partial class MainWindow
{
    // Currently displayed app collection in normal mode; tracked so the empty
    // state can react to add/delete without an explicit refresh call.
    private INotifyCollectionChanged? _boundAppCollection;

    #region Data Load/Save

    private void RefreshContentArea()
    {
        var source = _selectedFolder?.Apps ?? _ungroupedApps;

        if (!ReferenceEquals(source, _boundAppCollection))
        {
            if (_boundAppCollection is not null)
                _boundAppCollection.CollectionChanged -= OnShownAppsChanged;
            _boundAppCollection = source;
            _boundAppCollection.CollectionChanged += OnShownAppsChanged;
        }

        AppGridView.ItemsSource = source;
        ReleaseHiddenIcons();
        UpdateEmptyState();
    }

    private void OnShownAppsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        UpdateEmptyState();

    /// <summary>
    /// Shows a centered hint over the content area when the current view is empty:
    /// "no results" while searching, "no apps yet" otherwise.
    /// </summary>
    private void UpdateEmptyState()
    {
        bool searching = !string.IsNullOrEmpty(_searchText);

        // A debounced pass is queued, so _searchResults still holds the previous query's
        // hits (or nothing, on the first character). Announcing "no results" for that
        // window would flash the placeholder while the user is still typing.
        if (searching && _searchPending)
        {
            EmptyStateView.Visibility = Visibility.Collapsed;
            return;
        }

        bool empty = searching
            ? _searchResults.Count == 0
            : (_selectedFolder?.Apps ?? _ungroupedApps).Count == 0;

        if (empty)
            EmptyStateText.Text = Loc.GetString(searching ? "Empty_NoResults" : "Empty_NoApps");

        EmptyStateView.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        // "Add your first app" only makes sense for a genuinely empty folder — a search
        // that found nothing is a different situation and offering it there is noise.
        EmptyStateAddButton.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;
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
        {
            vm.ReleaseIcon();
            return;
        }

        BindContainerAutomationName(args.ItemContainer, vm, nameof(AppItemViewModel.DisplayName));
        _ = vm.LoadIconAsync();
    }

    /// <summary>
    /// Loads on realization like the grid above — a query can match every item in the
    /// workspace, and IconService has no in-memory cache, so loading the whole result set
    /// would be one disk read and decode per hit for the handful of rows on screen.
    /// <para>
    /// Unlike the grid it deliberately does <b>not</b> release on recycle. A result row and
    /// a grid tile are the same <see cref="AppItemViewModel"/> instance, and the grid is
    /// merely collapsed while search is up — its containers are never recycled, so nothing
    /// re-fires this to load the icon back. Releasing here blanked the tiles behind the
    /// results: on Esc when the whole list recycled at once, and on scrolling for any row
    /// that also lives in the open folder. Icons a search leaves resident are swept by
    /// ReleaseHiddenIcons on the next folder switch.
    /// </para>
    /// </summary>
    private void SearchResultsView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not AppItemViewModel vm) return;

        BindContainerAutomationName(args.ItemContainer, vm, nameof(AppItemViewModel.DisplayName));
        _ = vm.LoadIconAsync();
    }

    private void SidebarListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not FolderViewModel vm) return;
        BindContainerAutomationName(args.ItemContainer, vm, nameof(FolderViewModel.DisplayName));
    }

    /// <summary>
    /// Gives an item container a real accessible name. This has to happen on the
    /// container, not inside the item template: GridViewItem / ListViewItem derive from
    /// ContentControl, whose automation peer names itself from the *content's* plain
    /// text — for a templated item that is the view model's ToString(), so every tile
    /// announced itself as "ace_run.AppItemViewModel". An AutomationProperties.Name set
    /// on the template root only names a child of the container, and Setter.Value in the
    /// ItemContainerStyle cannot carry a Binding in WinUI, so this is code.
    ///
    /// A binding rather than a plain string, so a rename under a live container (edit
    /// dialog, folder rename) reaches the announced name. The view model is passed as an
    /// explicit <see cref="Binding.Source"/>: an item container is a ContentControl whose
    /// Content is the item, but its DataContext is not, so a source-less binding here
    /// resolves against nothing. Recycling is covered by re-binding on every realization.
    /// </summary>
    private static void BindContainerAutomationName(DependencyObject container, object source, string path)
    {
        if (container is not FrameworkElement element) return;

        element.SetBinding(AutomationProperties.NameProperty, new Binding
        {
            Path = new PropertyPath(path),
            Source = source,
            Mode = BindingMode.OneWay
        });
    }

    private void CommitSave()
    {
        _appData.Tags = _tags.Select(t => t.ToModel()).ToList();
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

    private FolderViewModel? FindFolderOfApp(AppItemViewModel app)
    {
        foreach (var folder in _folders)
            if (folder.Apps.Contains(app)) return folder;
        return null; // null = ungrouped
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

    #region Tags

    private IEnumerable<AppItemViewModel> AllApps()
    {
        foreach (var app in _ungroupedApps)
            yield return app;
        foreach (var folder in _folders)
            foreach (var app in folder.Apps)
                yield return app;
    }

    /// <summary>
    /// Reconciles every app with the current tag list: drops tags that no longer exist,
    /// removes duplicates, and re-sorts into workspace tag order. The fixed order is what
    /// lets the dots on two tiles carrying the same tags line up — it also means there is
    /// no per-item tag ordering for the user to manage.
    /// </summary>
    private void NormalizeAppTags()
    {
        foreach (var app in AllApps())
        {
            if (app.Tags.Count == 0) continue;

            var assigned = new HashSet<Guid>(app.Tags.Select(t => t.Id));
            var ordered = _tags.Where(t => assigned.Contains(t.Id)).ToList();

            if (!ordered.SequenceEqual(app.Tags))
                app.SetTags(ordered);
        }
    }

    /// <summary>Adds or removes one tag on an app, then persists.</summary>
    private void ToggleTagOnApp(AppItemViewModel app, TagViewModel tag, bool assign)
    {
        var assigned = new HashSet<Guid>(app.Tags.Select(t => t.Id));
        if (assign ? !assigned.Add(tag.Id) : !assigned.Remove(tag.Id))
            return;

        SetAppTags(app, _tags.Where(t => assigned.Contains(t.Id)));
    }

    private void ClearTagsOnApp(AppItemViewModel app)
    {
        if (app.Tags.Count == 0) return;
        SetAppTags(app, Array.Empty<TagViewModel>());
    }

    /// <summary>Assigns the given tags (kept in workspace order) and saves.</summary>
    private void SetAppTags(AppItemViewModel app, IEnumerable<TagViewModel> tags)
    {
        app.SetTags(tags);

        // Search mode blocks SaveItems(); commit directly so the change persists.
        if (!string.IsNullOrEmpty(_searchText))
            CommitSave();
        else
            SaveItems();
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

        if (await ShowModalAsync(dialog) != ContentDialogResult.Primary)
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

        if (await ShowModalAsync(dialog) != ContentDialogResult.Primary)
            return;

        foreach (var app in folder.Apps)
            _searchResults.Remove(app);

        _folders.Remove(folder);
        PruneHistory(folder.Id);

        // record: false — being pushed out of a folder that no longer exists is not a step
        // the user took, and Back must not offer to return to it.
        if (_selectedFolder == folder)
            NavigateToFolder(null, record: false);

        PurgeStaleRecentLaunches();
        CommitSave();
        ((App)Application.Current).UpdateTrayContextMenu();
    }

    #endregion
}
