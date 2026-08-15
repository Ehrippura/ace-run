using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using ace_run.Helpers;
using ace_run.Models;
using ace_run.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace ace_run;

public sealed partial class ManageWorkspacesDialog : ContentDialog
{
    private readonly nint _hwnd;
    private readonly Guid _activeWorkspaceId;

    /// <summary>
    /// A second copy of the config, loaded here and written back whole.
    /// </summary>
    /// <remarks>
    /// This is the one place that does not honour "settings have exactly one owner". It is safe
    /// only because <c>ManageWorkspacesButton_Click</c> brackets the dialog: <c>CommitSave()</c>
    /// before it opens flushes MainWindow's copy to disk, and <c>ReloadAfterWorkspaceManagement()</c>
    /// after it closes re-reads it. Anything that opens this dialog without both halves will
    /// silently discard whatever MainWindow had not yet saved.
    /// </remarks>
    private WorkspaceConfig _config;

    private readonly ObservableCollection<WorkspaceViewModel> _workspaceVMs = new();

    /// <summary>The row being renamed, captured at focus. See <see cref="CommitName"/>.</summary>
    private WorkspaceViewModel? _editing;
    private string _editingOriginal = string.Empty;

    public static string NameFieldLabel => Loc.GetString("Workspace_NameLabel");
    public static string MoreLabel => Loc.GetString("Row_More");
    public static string ReorderLabel => Loc.GetString("Row_Reorder");
    public static string ChooseColorLabel => Loc.GetString("Color_Choose");

    public ManageWorkspacesDialog(nint hwnd, Guid activeWorkspaceId)
    {
        _hwnd = hwnd;
        _activeWorkspaceId = activeWorkspaceId;
        _config = DataService.LoadConfig();

        InitializeComponent();

        Title = Loc.GetString("Workspace_ManageTitle");
        PrimaryButtonText = Loc.GetString("CloseButton");
        DefaultButton = ContentDialogButton.Primary;

        NewWorkspaceLabel.Text = Loc.GetString("Workspace_New");
        CreateBlankItem.Text = Loc.GetString("Workspace_CreateBlank");
        CopyCurrentItem.Text = Loc.GetString("Workspace_CopyCurrent");
        ImportLabel.Text = Loc.GetString("Workspace_Import");

        BuildWorkspaceList();
        WorkspaceListView.ItemsSource = _workspaceVMs;
    }

    private void BuildWorkspaceList()
    {
        _workspaceVMs.Clear();
        foreach (var ws in _config.Workspaces)
            _workspaceVMs.Add(new WorkspaceViewModel(ws) { IsActive = ws.Id == _activeWorkspaceId });
    }

    // ---- New workspace ----

    private void CreateBlank_Click(object sender, RoutedEventArgs e) => CreateWorkspace(copyCurrent: false);

    private void CopyCurrent_Click(object sender, RoutedEventArgs e) => CreateWorkspace(copyCurrent: true);

    /// <remarks>
    /// The row is created and named in place, rather than filled in on a form first. The form
    /// this replaces was where Enter closed the whole dialog, where the only label for the name
    /// was its placeholder, and where the colour choice lived in a hand-written list of English
    /// <c>ComboBoxItem</c>s that had already drifted out of step with <see cref="ColorKeys"/>.
    ///
    /// A new workspace therefore starts colourless — null is a real state for it, and the
    /// swatch on the row is one click away.
    /// </remarks>
    private void CreateWorkspace(bool copyCurrent)
    {
        ErrorBar.IsOpen = false;

        var appData = copyCurrent ? DataService.LoadWorkspace(_activeWorkspaceId) : new AppData();

        var info = new WorkspaceInfo
        {
            Name = UniqueName(Loc.GetString("Workspace_DefaultName")),
            AppCount = appData.ItemCount
        };

        _config.Workspaces.Add(info);
        DataService.SaveWorkspace(info.Id, appData);
        DataService.SaveConfig(_config);

        var vm = new WorkspaceViewModel(info);
        _workspaceVMs.Add(vm);
        FocusNameBox(vm);
    }

    /// <summary>The given name, or the first free "<c>name N</c>" after it.</summary>
    private string UniqueName(string basis)
    {
        if (!IsTaken(basis, null)) return basis;

        for (var n = 2; ; n++)
        {
            var candidate = $"{basis} {n}";
            if (!IsTaken(candidate, null)) return candidate;
        }
    }

    private bool IsTaken(string name, WorkspaceViewModel? exclude) =>
        _workspaceVMs.Any(w => !ReferenceEquals(w, exclude)
                               && string.Equals(w.Name, name, StringComparison.CurrentCultureIgnoreCase));

    /// <summary>
    /// Focuses a row's name box, retrying once on the dispatcher — <c>ContainerFromItem</c>
    /// answers null straight after an <c>Add</c>, before layout has run.
    /// </summary>
    private void FocusNameBox(WorkspaceViewModel vm)
    {
        WorkspaceListView.ScrollIntoView(vm);
        WorkspaceListView.UpdateLayout();

        if (!TryFocusNameBox(vm))
            DispatcherQueue.TryEnqueue(() => TryFocusNameBox(vm));
    }

    private bool TryFocusNameBox(WorkspaceViewModel vm)
    {
        if (WorkspaceListView.ContainerFromItem(vm) is not DependencyObject container) return false;
        if (VisualTree.FindDescendant<TextBox>(container) is not { } box) return false;

        box.Focus(FocusState.Programmatic);
        box.SelectAll();
        return true;
    }

    /// <summary>
    /// Names the row container for a screen reader. Without it a row announces itself as
    /// "ace_run.WorkspaceViewModel" — see <see cref="ItemContainers.BindAutomationName"/>.
    /// </summary>
    private void WorkspaceListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not WorkspaceViewModel vm) return;
        ItemContainers.BindAutomationName(args.ItemContainer, vm, nameof(WorkspaceViewModel.Name));
    }

    // ---- Colour ----

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: WorkspaceViewModel vm } button) return;

        // allowNone: true — ColorTag is nullable and null is the documented "no colour, no
        // window edge" state, so a workspace can genuinely be put back to having none.
        ColorSwatchFlyout.Show(button, vm.ColorTag, allowNone: true, key =>
        {
            vm.ColorTag = key;

            // The view model writes straight into the WorkspaceInfo held by _config, so the
            // config is dirty the moment the setter runs. The live window edge repaints when
            // the dialog closes and ReloadAfterWorkspaceManagement re-applies the identity.
            DataService.SaveConfig(_config);
        });
    }

    // ---- Overflow menu ----

    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: WorkspaceViewModel vm } button) return;

        var index = _workspaceVMs.IndexOf(vm);

        ManageRowMenu.Show(
            button,
            onExport: () => _ = ExportWorkspaceAsync(vm),
            onMoveUp: () => MoveRow(vm, -1),
            onMoveDown: () => MoveRow(vm, 1),
            canMoveUp: index > 0,
            canMoveDown: index >= 0 && index < _workspaceVMs.Count - 1,
            onDelete: () => RequestDelete(button, vm));
    }

    private void MoveRow(WorkspaceViewModel vm, int delta)
    {
        if (ItemOrdering.MoveBy(_workspaceVMs, vm, delta))
            PersistWorkspaceOrder();
    }

    /// <summary>
    /// Rebuilds the persisted list from the view models' order.
    /// </summary>
    /// <remarks>
    /// Identity survives: <c>ToInfo()</c> hands back the very <see cref="WorkspaceInfo"/> the
    /// view model wraps, so this reorders and changes nothing else. It also re-maps Ctrl+1..9,
    /// which indexes the same list — intended, and now reachable by menu rather than only by a
    /// drag that was nearly impossible to start.
    /// </remarks>
    private void PersistWorkspaceOrder()
    {
        _config.Workspaces = _workspaceVMs.Select(vm => vm.ToInfo()).ToList();
        DataService.SaveConfig(_config);
    }

    private void WorkspaceListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) =>
        PersistWorkspaceOrder();

    // ---- Import ----

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, _hwnd);
        picker.FileTypeFilter.Add(".acerun");

        StorageFile? file;
        try { file = await picker.PickSingleFileAsync(); }
        catch { return; }

        if (file is null) return;

        try
        {
            var json = await FileIO.ReadTextAsync(file);
            switch (WorkspaceImport.TryParse(json, out var export))
            {
                case ImportRejection.NotAnAceRunFile:
                    ShowError(Loc.GetString("Workspace_InvalidFile"));
                    return;
                case ImportRejection.NewerVersion:
                    ShowError(Loc.GetString("Workspace_ImportTooNew"));
                    return;
            }

            var info = new WorkspaceInfo
            {
                // An export written without a name gets the same default a workspace created by
                // hand would, rather than appearing in the picker as a blank row. Deduped for
                // the same reason a new row is: two identical names in the picker are unusable.
                Name = UniqueName(string.IsNullOrWhiteSpace(export!.Name)
                    ? Loc.GetString("Workspace_DefaultName")
                    : export.Name.Trim()),
                ColorTag = export.ColorTag,
                AppCount = export.AppData.ItemCount
            };

            _config.Workspaces.Add(info);
            DataService.SaveWorkspace(info.Id, export.AppData);
            DataService.SaveConfig(_config);

            _workspaceVMs.Add(new WorkspaceViewModel(info));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Import failed: {ex.Message}");
            ShowError(Loc.GetString("Workspace_InvalidFile"));
        }
    }

    // ---- Export ----

    private async System.Threading.Tasks.Task ExportWorkspaceAsync(WorkspaceViewModel vm)
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, _hwnd);
        picker.SuggestedFileName = vm.Name;
        picker.FileTypeChoices.Add("Ace Run Workspace", new List<string> { ".acerun" });

        StorageFile? file;
        try { file = await picker.PickSaveFileAsync(); }
        catch { return; }

        if (file is null) return;

        try
        {
            var appData = DataService.LoadWorkspace(vm.Id);
            var export = new WorkspaceExport
            {
                Name = vm.Name,
                ColorTag = vm.ColorTag,
                AppData = appData
            };
            await FileIO.WriteTextAsync(file, DataService.SerializeExport(export));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Export failed: {ex.Message}");
            ShowError(ex.Message);
        }
    }

    // ---- Delete ----

    private void RequestDelete(Button anchor, WorkspaceViewModel vm)
    {
        if (_config.Workspaces.Count <= 1)
        {
            ShowError(Loc.GetString("Workspace_CannotDeleteLast"));
            return;
        }

        ManageRowMenu.ConfirmDelete(
            anchor,
            Loc.GetString("Workspace_DeleteTitle"),
            string.Format(Loc.GetString("Workspace_DeleteConfirm"), vm.Name),
            () => PerformDelete(vm));
    }

    private void PerformDelete(WorkspaceViewModel vm)
    {
        _config.Workspaces.RemoveAll(w => w.Id == vm.Id);

        if (_config.ActiveWorkspaceId == vm.Id)
            _config.ActiveWorkspaceId = _config.Workspaces[0].Id;

        // Icons are cached per item id, and the workspace file is the last thing on disk that
        // still knows those ids — read them out before the delete or the cache entries are
        // orphaned for good. A workspace that fails to load yields nothing here, which leaks
        // rather than deletes: the safe direction to fail in.
        IconService.InvalidateCache(AppDataQuery.ItemIds(DataService.LoadWorkspace(vm.Id)));

        DataService.DeleteWorkspace(vm.Id);
        DataService.SaveConfig(_config);
        _workspaceVMs.Remove(vm);
    }

    // ---- Inline rename ----

    private void WorkspaceName_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb) return;

        _editing = tb.DataContext as WorkspaceViewModel;
        _editingOriginal = _editing?.Name ?? string.Empty;
    }

    private void WorkspaceName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb) return;

        var vm = _editing;
        _editing = null;
        CommitName(tb, vm);
    }

    private void WorkspaceName_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox tb) return;

        // Handled on purpose: unhandled, Enter reaches the dialog's DefaultButton — which is
        // Close — and Escape reaches its cancel path, so finishing a rename the obvious way
        // used to shut the dialog.
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            CommitName(tb, _editing);
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            tb.Text = _editingOriginal;
        }
    }

    /// <summary>
    /// Writes the edited name back, or explains why it will not.
    /// </summary>
    /// <remarks>
    /// <paramref name="vm"/> is the row captured at <c>GotFocus</c>. Reading
    /// <c>tb.DataContext</c> here instead is a data-corruption path: a delete shifts every
    /// container below it and a drag reorder mutates the source with RemoveAt + Insert, so a
    /// container can be recycled onto a different workspace between the edit and the commit.
    /// </remarks>
    private void CommitName(TextBox tb, WorkspaceViewModel? vm)
    {
        if (vm is null || !_workspaceVMs.Contains(vm) || !ReferenceEquals(tb.DataContext, vm)) return;

        var newName = tb.Text.Trim();
        if (newName == vm.Name) return;

        if (string.IsNullOrEmpty(newName))
        {
            // Put the old name back rather than just declining the edit — returning here left
            // the box blank while the workspace kept its name, and the two disagreed until
            // something else happened to redraw the row.
            tb.Text = vm.Name;
            return;
        }

        if (IsTaken(newName, vm))
        {
            ShowError(string.Format(Loc.GetString("Workspace_DuplicateName"), newName));
            tb.Text = vm.Name;
            return;
        }

        ErrorBar.IsOpen = false;
        vm.Name = newName;
        _editingOriginal = newName;

        var info = _config.Workspaces.FirstOrDefault(w => w.Id == vm.Id);
        if (info is not null)
        {
            info.LastModifiedAt = DateTime.UtcNow;
            DataService.SaveConfig(_config);
        }
    }

    // ---- Helper ----

    private void ShowError(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }
}
