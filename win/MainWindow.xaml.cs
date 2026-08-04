using ace_run.Models;
using ace_run.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Graphics;

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

        ApplyInitialWindowSize();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        // TitleBar grows to 48px once Content/headers are populated; without this the
        // system caption buttons stay 32px tall against a 48px band.
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        // Must run before ItemsSource: the rail's selection indicator resolves its brush
        // when the item template is applied.
        InitializeWorkspaceBrush();

        UngroupedItemLabel.Text = Loc.GetString("UngroupedFolderName");
        SidebarListView.ItemsSource = _folders;
        SearchResultsView.ItemsSource = _searchResults;
        WorkspaceComboBox.ItemsSource = _workspaces;

        ManageWorkspacesMenuItem.Text = Loc.GetString("Workspace_Manage");
        ManageTagsMenuItem.Text = Loc.GetString("Tag_Manage");

        // Accessible name for the icon-only button (screen readers, UIA). Its own label,
        // not "Manage Workspaces" — the menu behind it covers workspaces and tags both.
        var manage = Loc.GetString("SettingsButton_Label");
        ToolTipService.SetToolTip(SettingsButton, manage);
        AutomationProperties.SetName(SettingsButton, manage);

        _searchResults.CollectionChanged += OnShownAppsChanged;

        // The ungrouped row is a ListView.Header, not a FolderViewModel, so its count has
        // no binding to ride on.
        _ungroupedApps.CollectionChanged += (_, _) => UpdateUngroupedCount();
        UpdateUngroupedCount();

        RootGrid.SizeChanged += (_, e) => UpdateRailForWidth(e.NewSize.Width);

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

    // Default window size, in DIPs. A nav-pane + content silhouette; wide enough for
    // the rail plus a comfortable multi-column tile grid.
    private const int DefaultWidthDip = 1120;
    private const int DefaultHeightDip = 760;
    private const int MinWidthDip = 720;
    private const int MinHeightDip = 480;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    /// <summary>
    /// Sizes the window in the constructor, before it is shown. The persisted size used
    /// to be applied only after the first workspace finished loading, which produced a
    /// visible resize pop on every launch.
    /// </summary>
    private void ApplyInitialWindowSize()
    {
        var hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        var scale = GetDpiForWindow(hwnd) / 96.0;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(MinWidthDip * scale);
            presenter.PreferredMinimumHeight = (int)(MinHeightDip * scale);
        }

        // Read config directly rather than waiting for InitializeWorkspacesAsync; this is
        // a small synchronous file read and it has to happen before the window is shown.
        var saved = DataService.LoadConfig().WindowState;

        var width = saved is { Width: > 0 } ? saved.Width : (int)(DefaultWidthDip * scale);
        var height = saved is { Height: > 0 } ? saved.Height : (int)(DefaultHeightDip * scale);

        // Clamp to the current monitor: a size saved on a 4K display would otherwise
        // restore larger than a 1080p screen and put the controls out of reach.
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        width = Math.Min(width, workArea.Width);
        height = Math.Min(height, workArea.Height);

        AppWindow.Resize(new SizeInt32(width, height));
    }

    #endregion

    private void UpdateUngroupedCount() =>
        UngroupedItemCount.Text = _ungroupedApps.Count.ToString();

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
