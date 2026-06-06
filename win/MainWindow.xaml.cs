using ace_run.Models;
using ace_run.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace ace_run;

public sealed partial class MainWindow : Window
{
    private readonly ObservableCollection<FolderViewModel> _folders = new();
    private readonly ObservableCollection<AppItemViewModel> _ungroupedApps = new();
    private readonly ObservableCollection<AppItemViewModel> _searchResults = new();
    private readonly ObservableCollection<WorkspaceViewModel> _workspaces = new();
    private readonly ObservableCollection<TagViewModel> _tags = new();

    private FolderViewModel? _selectedFolder; // null = ungrouped
    private AppData _appData = new();
    private string _searchText = string.Empty;

    private WorkspaceConfig _workspaceConfig = new();
    private WorkspaceInfo _currentWorkspace = new();
    private bool _suppressWorkspaceSwitch;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;

        UngroupedItemLabel.Text = Loc.GetString("UngroupedFolderName");
        SidebarListView.ItemsSource = _folders;
        SearchResultsView.ItemsSource = _searchResults;
        WorkspaceComboBox.ItemsSource = _workspaces;
        ToolTipService.SetToolTip(ManageTagsButton, Loc.GetString("Tag_Manage"));

        _ = InitializeWorkspacesAsync();
        Closed += MainWindow_Closed;
    }

    #region Window Lifecycle

    public void AttachContextMenus()
    {
        AppGridView.PreviewKeyDown += AppGridView_KeyDown;
        SidebarListView.RightTapped += SidebarListView_RightTapped;
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        SaveWindowSize();

        if (App.TrayEnabled)
        {
            CommitSave();
            args.Handled = true;
            this.AppWindow.Hide();
            return;
        }

        CommitSave();
    }

    private void SaveWindowSize()
    {
        var size = AppWindow.Size;
        if (size.Width <= 0 || size.Height <= 0) return;
        _workspaceConfig.WindowState = new Models.WindowState { Width = size.Width, Height = size.Height };
        DataService.SaveConfig(_workspaceConfig);
    }

    private void RestoreWindowSize()
    {
        var ws = _workspaceConfig.WindowState;
        if (ws is not null && ws.Width > 0 && ws.Height > 0)
            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(ws.Width, ws.Height));
    }

    #endregion

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(child);
        while (parent is not null)
        {
            if (parent is T t) return t;
            parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent);
        }
        return null;
    }
}
