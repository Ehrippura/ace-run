using ace_run.Models;
using ace_run.Services;
using Microsoft.UI.Dispatching;
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

    /// <summary>
    /// Icon teardown for items that are going away for good, as opposed to merely leaving the
    /// screen: drops the bitmap and the disk cache entry both.
    ///
    /// Deletion is the one place the cache file can be orphaned, because it is keyed by
    /// <see cref="AppItemViewModel.Id"/> and nothing else on disk remembers that id once the
    /// item is out of the workspace JSON. <see cref="ReleaseHiddenIcons"/> cannot cover the
    /// memory half either — it walks the collections an item has just been removed from.
    /// </summary>
    private static void DiscardIcons(IEnumerable<AppItemViewModel> apps)
    {
        foreach (var app in apps)
        {
            app.ReleaseIcon();
            IconService.InvalidateCache(app.Id);
        }
    }

    private void AppGridView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is not AppItemViewModel vm) return;
        if (args.InRecycleQueue)
        {
            ScheduleIconRelease(vm);
            return;
        }

        BindContainerAutomationName(args.ItemContainer, vm, nameof(AppItemViewModel.DisplayName));
        _ = vm.LoadIconAsync();
    }

    /// <summary>
    /// Releases a recycled tile's icon — but only once nothing is showing that item any more.
    ///
    /// The icon lives on the view model, which is shared by every container that ever shows
    /// it, so a recycle notification is <b>not</b> proof the item left the screen. A drag
    /// reorder takes the item out of the collection and puts it back at the new index, and
    /// the realization of the new container and the recycling of the old one land in the same
    /// layout pass in no fixed order — releasing straight from the recycle branch therefore
    /// blanked a tile that was on screen. It only misfired sometimes because the new
    /// container's own LoadIconAsync usually finished after the release and quietly put the
    /// icon back; when the disk read won that race the tile stayed blank until the next
    /// folder switch.
    ///
    /// Deferring lets the reorder settle, and ContainerFromItem then answers the only
    /// question that matters. Scrolled-away items still resolve to null and are released as
    /// before, and a mistimed release is self-correcting: realization reloads the icon.
    /// </summary>
    private void ScheduleIconRelease(AppItemViewModel vm) =>
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (AppGridView.ContainerFromItem(vm) is null)
                vm.ReleaseIcon();
        });

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
            info.AppCount = _appData.ItemCount;
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

    /// <summary>
    /// Saves an edit that can be made from *either* view. SaveItems() returns early while a
    /// query is active, so every flow reachable from the search results — launching, tagging,
    /// moving, deleting — has to commit directly instead. Nothing else distinguishes the two.
    /// </summary>
    private void PersistAfterEdit()
    {
        if (!string.IsNullOrEmpty(_searchText))
            CommitSave();
        else
            SaveItems();
    }

    private bool PurgeStaleRecentLaunches()
    {
        var liveIds = new HashSet<Guid>();
        foreach (var app in AllApps())
            liveIds.Add(app.Id);

        return RecentLaunchList.Purge(_appData.RecentLaunches, liveIds);
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
            var ordered = TagOrdering.Normalize(_tags, app.Tags);
            if (ordered is not null)
                app.SetTags(ordered);
        }
    }

    /// <summary>
    /// Adds or removes one tag across a batch, then persists once.
    /// </summary>
    private void SetTagOnApps(IList<AppItemViewModel> apps, TagViewModel tag, bool assign)
    {
        var changed = false;

        foreach (var app in apps)
        {
            var ordered = TagOrdering.WithTagToggled(_tags, app.Tags, tag.Id, assign);
            if (ordered is null) continue; // already in the requested state

            app.SetTags(ordered);
            changed = true;
        }

        if (changed)
            PersistAfterEdit();
    }

    private void ClearTagsOnApps(IList<AppItemViewModel> apps)
    {
        var changed = false;

        foreach (var app in apps)
        {
            if (app.Tags.Count == 0) continue;
            app.SetTags(Array.Empty<TagViewModel>());
            changed = true;
        }

        if (changed)
            PersistAfterEdit();
    }

    /// <summary>Assigns the given tags (kept in workspace order) and saves.</summary>
    private void SetAppTags(AppItemViewModel app, IEnumerable<TagViewModel> tags)
    {
        app.SetTags(tags);
        PersistAfterEdit();
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

        DiscardIcons(targets);
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

        // Deleting a folder deletes the items in it — nothing else holds a reference to
        // folder.Apps — so their icons go the same way an explicit item delete sends them.
        DiscardIcons(folder.Apps);

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
