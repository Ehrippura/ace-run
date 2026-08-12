using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using ace_run.Models;
using ace_run.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ace_run;

public sealed partial class ManageWorkspacesDialog : ContentDialog
{
    private readonly nint _hwnd;
    private readonly Guid _activeWorkspaceId;
    private WorkspaceConfig _config;
    private readonly ObservableCollection<WorkspaceViewModel> _workspaceVMs = new();

    public ManageWorkspacesDialog(nint hwnd, Guid activeWorkspaceId)
    {
        _hwnd = hwnd;
        _activeWorkspaceId = activeWorkspaceId;
        _config = DataService.LoadConfig();

        InitializeComponent();

        Title = Loc.GetString("Workspace_ManageTitle");
        PrimaryButtonText = Loc.GetString("CloseButton");
        DefaultButton = ContentDialogButton.Primary;

        // Localize static labels
        NewWorkspaceLabel.Text = Loc.GetString("Workspace_New");
        ImportLabel.Text = Loc.GetString("Workspace_Import");
        NewFormTitle.Text = Loc.GetString("Workspace_NewTitle");
        BlankRadio.Content = Loc.GetString("Workspace_CreateBlank");
        CopyRadio.Content = Loc.GetString("Workspace_CopyCurrent");
        ConfirmNewBtn.Content = Loc.GetString("SaveButton");
        CancelNewBtn.Content = Loc.GetString("CancelButton");

        BuildWorkspaceList();
        WorkspaceListView.ItemsSource = _workspaceVMs;
    }

    private void BuildWorkspaceList()
    {
        _workspaceVMs.Clear();
        foreach (var ws in _config.Workspaces)
            _workspaceVMs.Add(new WorkspaceViewModel(ws));
    }

    // ---- New workspace (inline form) ----

    private void NewWorkspaceButton_Click(object sender, RoutedEventArgs e)
    {
        NewNameBox.Text = string.Empty;
        ColorCombo.SelectedIndex = 0;
        BlankRadio.IsChecked = true;
        NewWorkspaceForm.Visibility = Visibility.Visible;
        NewNameBox.Focus(FocusState.Programmatic);
        ErrorBar.IsOpen = false;
    }

    /// <summary>
    /// A trimmed name, or the localized default when the user left the box empty.
    /// </summary>
    /// <remarks>
    /// <c>Workspace_DefaultName</c> and not <c>Workspace_New</c>: the latter is the button
    /// that opens this form. This used to be a hardcoded English literal, so a Chinese or
    /// Japanese user who skipped the name got an English workspace.
    /// </remarks>
    private static string DefaultedName(string? input) =>
        string.IsNullOrWhiteSpace(input) ? Loc.GetString("Workspace_DefaultName") : input.Trim();

    private void ConfirmNewWorkspace_Click(object sender, RoutedEventArgs e)
    {
        var name = DefaultedName(NewNameBox.Text);
        var colorTag = ColorTagFromCombo();

        AppData appData = CopyRadio.IsChecked == true
            ? DataService.LoadWorkspace(_activeWorkspaceId)
            : new AppData();

        var wsInfo = new WorkspaceInfo
        {
            Name = name,
            ColorTag = colorTag,
            AppCount = appData.ItemCount
        };

        _config.Workspaces.Add(wsInfo);
        DataService.SaveWorkspace(wsInfo.Id, appData);
        DataService.SaveConfig(_config);

        _workspaceVMs.Add(new WorkspaceViewModel(wsInfo));
        NewWorkspaceForm.Visibility = Visibility.Collapsed;
    }

    private void CancelNewWorkspace_Click(object sender, RoutedEventArgs e)
    {
        NewWorkspaceForm.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Reads the selected color key from <c>Tag</c>, never from <c>Content</c>. The
    /// returned string is persisted to config.json, so it must stay independent of the
    /// display language. The "None" item has no Tag and yields null.
    /// </summary>
    private string? ColorTagFromCombo()
    {
        var item = ColorCombo.SelectedItem as ComboBoxItem;
        return item?.Tag as string;
    }

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

            var wsInfo = new WorkspaceInfo
            {
                // An export written without a name gets the same default a workspace created
                // by hand would, rather than appearing in the picker as a blank row.
                Name = DefaultedName(export!.Name),
                ColorTag = export.ColorTag,
                AppCount = export.AppData.ItemCount
            };

            _config.Workspaces.Add(wsInfo);
            DataService.SaveWorkspace(wsInfo.Id, export.AppData);
            DataService.SaveConfig(_config);

            _workspaceVMs.Add(new WorkspaceViewModel(wsInfo));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Import failed: {ex.Message}");
            ShowError(Loc.GetString("Workspace_InvalidFile"));
        }
    }

    // ---- Export ----

    private async void ExportWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: WorkspaceViewModel vm }) return;

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
            var json = DataService.SerializeExport(export);
            await FileIO.WriteTextAsync(file, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Export failed: {ex.Message}");
            ShowError(ex.Message);
        }
    }

    // ---- Delete (with Flyout confirmation, no nested ContentDialog) ----

    private void DeleteWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button deleteBtn || deleteBtn.Tag is not WorkspaceViewModel vm) return;

        if (_config.Workspaces.Count <= 1)
        {
            ShowError(Loc.GetString("Workspace_CannotDeleteLast"));
            return;
        }

        ConfirmFlyout.Show(
            deleteBtn,
            string.Format(Loc.GetString("Workspace_DeleteConfirm"), vm.Name),
            Loc.GetString("DeleteButton"),
            () => PerformDelete(vm));
    }

    private void PerformDelete(WorkspaceViewModel vm)
    {
        _config.Workspaces.RemoveAll(w => w.Id == vm.Id);

        if (_config.ActiveWorkspaceId == vm.Id)
            _config.ActiveWorkspaceId = _config.Workspaces[0].Id;

        // Icons are cached per item id, and the workspace file is the last thing on disk that
        // still knows those ids — read them out before the delete or the PNGs are orphaned
        // for good. A workspace that fails to load yields nothing here, which leaks rather
        // than deletes: the safe direction to fail in.
        IconService.InvalidateCache(AppDataQuery.ItemIds(DataService.LoadWorkspace(vm.Id)));

        DataService.DeleteWorkspace(vm.Id);
        DataService.SaveConfig(_config);
        _workspaceVMs.Remove(vm);
    }

    // ---- Inline rename ----

    private void WorkspaceName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb) return;

        var vm = tb.DataContext as WorkspaceViewModel;
        if (vm is null) return;

        var newName = tb.Text.Trim();
        if (string.IsNullOrEmpty(newName))
        {
            // Put the old name back rather than just declining the edit. Returning here left
            // the box showing blank while the workspace kept its name — the two disagreed
            // until something else happened to redraw the row. ManageTagsDialog has always
            // done it this way.
            tb.Text = vm.Name;
            return;
        }

        if (newName == vm.Name) return;

        vm.Name = newName;

        var info = _config.Workspaces.FirstOrDefault(w => w.Id == vm.Id);
        if (info is not null)
        {
            info.LastModifiedAt = DateTime.UtcNow;
            DataService.SaveConfig(_config);
        }
    }

    // ---- Drag reorder ----

    private void WorkspaceListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        _config.Workspaces = _workspaceVMs.Select(vm => vm.ToInfo()).ToList();
        DataService.SaveConfig(_config);
    }

    // ---- Helper ----

    private void ShowError(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }
}
