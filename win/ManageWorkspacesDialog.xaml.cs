using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using ace_run.Models;
using ace_run.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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

    private static readonly JsonSerializerOptions s_options = new() { WriteIndented = true };

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
            _workspaceVMs.Add(new WorkspaceViewModel(ws, _config.DefaultWorkspaceId == ws.Id));
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

    private void ConfirmNewWorkspace_Click(object sender, RoutedEventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(NewNameBox.Text) ? "New Workspace" : NewNameBox.Text.Trim();
        var colorTag = ColorTagFromCombo();

        AppData appData = CopyRadio.IsChecked == true
            ? DataService.LoadWorkspace(_activeWorkspaceId)
            : new AppData();

        var wsInfo = new WorkspaceInfo
        {
            Name = name,
            ColorTag = colorTag,
            AppCount = appData.UngroupedItems.Count + appData.Folders.Sum(f => f.Children.Count)
        };

        _config.Workspaces.Add(wsInfo);
        DataService.SaveWorkspace(wsInfo.Id, appData);
        DataService.SaveConfig(_config);

        _workspaceVMs.Add(new WorkspaceViewModel(wsInfo, false));
        NewWorkspaceForm.Visibility = Visibility.Collapsed;
    }

    private void CancelNewWorkspace_Click(object sender, RoutedEventArgs e)
    {
        NewWorkspaceForm.Visibility = Visibility.Collapsed;
    }

    private string? ColorTagFromCombo()
    {
        var item = ColorCombo.SelectedItem as ComboBoxItem;
        var text = item?.Content as string;
        return (text == "None" || text is null) ? null : text;
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
            var export = JsonSerializer.Deserialize<WorkspaceExport>(json, s_options);

            if (export?.AppData is null)
            {
                ShowError(Loc.GetString("Workspace_InvalidFile"));
                return;
            }

            var wsInfo = new WorkspaceInfo
            {
                Name = export.Name,
                ColorTag = export.ColorTag,
                AppCount = export.AppData.UngroupedItems.Count + export.AppData.Folders.Sum(f => f.Children.Count)
            };

            _config.Workspaces.Add(wsInfo);
            DataService.SaveWorkspace(wsInfo.Id, export.AppData);
            DataService.SaveConfig(_config);

            _workspaceVMs.Add(new WorkspaceViewModel(wsInfo, false));
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
            var json = JsonSerializer.Serialize(export, s_options);
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

        var panel = new StackPanel { Spacing = 8, Padding = new Thickness(4) };
        panel.Children.Add(new TextBlock
        {
            Text = string.Format(Loc.GetString("Workspace_DeleteConfirm"), vm.Name),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 220
        });

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        Flyout? flyout = null;

        var confirmBtn = new Button { Content = Loc.GetString("DeleteButton") };
        confirmBtn.Click += (_, _) =>
        {
            flyout?.Hide();
            PerformDelete(vm);
        };

        var cancelBtn = new Button { Content = Loc.GetString("CancelButton") };
        cancelBtn.Click += (_, _) => flyout?.Hide();

        btnRow.Children.Add(confirmBtn);
        btnRow.Children.Add(cancelBtn);
        panel.Children.Add(btnRow);

        flyout = new Flyout { Content = panel };
        flyout.ShowAt(deleteBtn);
    }

    private void PerformDelete(WorkspaceViewModel vm)
    {
        _config.Workspaces.RemoveAll(w => w.Id == vm.Id);

        if (_config.ActiveWorkspaceId == vm.Id)
            _config.ActiveWorkspaceId = _config.Workspaces[0].Id;

        if (_config.DefaultWorkspaceId == vm.Id)
            _config.DefaultWorkspaceId = _config.Workspaces[0].Id;

        DataService.DeleteWorkspace(vm.Id);
        DataService.SaveConfig(_config);
        _workspaceVMs.Remove(vm);
    }

    // ---- Default toggle ----

    private void DefaultToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: WorkspaceViewModel vm }) return;

        _config.DefaultWorkspaceId = vm.Id;
        DataService.SaveConfig(_config);

        foreach (var wsVm in _workspaceVMs)
            wsVm.IsDefault = wsVm.Id == vm.Id;
    }

    // ---- Inline rename ----

    private void WorkspaceName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb) return;

        var vm = tb.DataContext as WorkspaceViewModel;
        if (vm is null) return;

        var newName = tb.Text.Trim();
        if (string.IsNullOrEmpty(newName) || newName == vm.Name) return;

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
