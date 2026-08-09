using ace_run.Models;
using ace_run.Services;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
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
    private DispatcherQueueTimer? _searchDebounce;
    // A filter pass is queued but has not run: the query is live, the result list is not.
    private bool _searchPending;

    private WorkspaceConfig _workspaceConfig = new();
    private WorkspaceInfo _currentWorkspace = new();
    private bool _suppressWorkspaceSwitch;

    public MainWindow()
    {
        InitializeComponent();

        ApplyInitialWindowSize();

        // Alt+Tab and the taskbar read the window's own icon, which WinUI never sets.
        WindowIconService.Apply(this);

        ExtendsContentIntoTitleBar = true;
        // The chrome row is 48px tall; without this the system caption buttons stay 32px
        // tall against a 48px band. Set before InitializeTitleBar so the first inset pass
        // reads the tall height rather than the standard one.
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        InitializeTitleBar();

        // Must run before ItemsSource: the rail's selection indicator resolves its brush
        // when the item template is applied.
        InitializeWorkspaceBrush();

        UngroupedItemLabel.Text = Loc.GetString("UngroupedFolderName");
        // The row is a ListViewItem whose content is a Grid, so it has no plain text of
        // its own to name itself from. Folder rows get theirs from the container binding
        // in SidebarListView_ContainerContentChanging; this one is a header, not an item.
        AutomationProperties.SetName(UngroupedItem, UngroupedItemLabel.Text);
        SidebarListView.ItemsSource = _folders;
        SearchResultsView.ItemsSource = _searchResults;
        WorkspaceComboBox.ItemsSource = _workspaces;

        ManageWorkspacesMenuItem.Text = Loc.GetString("Workspace_Manage");
        ManageTagsMenuItem.Text = Loc.GetString("Tag_Manage");
        SettingsMenuItem.Text = Loc.GetString("Settings_Title");

        // Ctrl+, opens Settings itself, not this menu, so the hint belongs on the entry that
        // does the same thing. Text override rather than a real KeyboardAccelerator on the
        // item: the accelerator lives on RootGrid, and a flyout item's visual tree does not
        // exist until the flyout has been opened once.
        SettingsMenuItem.KeyboardAcceleratorTextOverride = "Ctrl+,";

        // Accessible name for the icon-only button (screen readers, UIA). Its own label,
        // not "Manage Workspaces" — the menu behind it covers workspaces and tags both.
        var manage = Loc.GetString("SettingsButton_Label");
        ToolTipService.SetToolTip(SettingsButton, manage);
        AutomationProperties.SetName(SettingsButton, manage);

        InstallCodeAccelerators();
        InitializeSearch();
        InitializeNavigationHistory();
        InitializeSettings();

        _searchResults.CollectionChanged += OnShownAppsChanged;

        // The ungrouped row is a ListView.Header, not a FolderViewModel, so its count has
        // no binding to ride on.
        _ungroupedApps.CollectionChanged += (_, _) => UpdateUngroupedCount();
        UpdateUngroupedCount();

        // Maximise/restore is the only thing that changes the window's corner shape, and
        // it always changes the size — so this one subscription covers all three jobs.
        // Maximising also changes the caption height, which the inset pass owns.
        RootGrid.SizeChanged += (_, e) =>
        {
            UpdateRailForWidth(e.NewSize.Width);
            UpdateWindowEdgeCorners();
            UpdateTitleBarInsets();
            UpdateTitleBarPassthrough();
        };

        _ = InitializeWorkspacesAsync();
        Closed += MainWindow_Closed;
    }

    #region Window Lifecycle

    public void AttachContextMenus()
    {
        SidebarListView.RightTapped += SidebarListView_RightTapped;
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        // Leave no debounce timer armed against a window that is closing (or going to the
        // tray). Flushing rather than stopping keeps _searchPending from being stranded
        // true, which would suppress the empty-state placeholder after the window returns.
        FlushPendingSearch();

        SaveWindowSize();

        if (App.TrayEnabled && Settings.CloseToTray)
        {
            CommitSave();
            args.Handled = true;
            this.AppWindow.Hide();
            return;
        }

        CommitSave();

        // Letting the window close is not the same as quitting: the tray icon keeps a
        // message loop alive, so without this the process lingers with no window. Before
        // the close-to-tray setting existed this branch was only reachable when the tray
        // icon had failed to initialize, which is why it went unnoticed.
        if (App.TrayEnabled)
            ((App)Application.Current).ExitApp(closeWindow: false);
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

    /// <summary>
    /// First descendant of the given type, breadth-first-ish. The counterpart to
    /// <see cref="FindParent{T}"/>, and the only way to reach a template part from outside
    /// the control — <c>GetTemplateChild</c> is protected and <c>FindName</c> does not cross
    /// into a template's namescope.
    /// </summary>
    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } nested) return nested;
        }
        return null;
    }

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
