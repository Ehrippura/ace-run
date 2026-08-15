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
using System.Threading.Tasks;
using Windows.Graphics;
using WinRT.Interop;

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
        InitializeMinimumSizeTracking();

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

    /// <summary>
    /// Persists the size in DIPs, converted from the physical pixels <c>AppWindow</c> reports.
    /// The scale is read live rather than reused from startup: the window may have been dragged
    /// to a display at another DPI since, and storing raw pixels is what used to make a size
    /// saved on one monitor restore wrong on the other.
    /// </summary>
    private void SaveWindowSize()
    {
        var size = AppWindow.Size;
        if (size.Width <= 0 || size.Height <= 0) return;

        var scale = CurrentScale;
        if (scale <= 0) return;

        _workspaceConfig.WindowState = new Models.WindowState
        {
            WidthDip = WindowPlacement.ToDip(size.Width, scale),
            HeightDip = WindowPlacement.ToDip(size.Height, scale),
        };
        DataService.SaveConfig(_workspaceConfig);
    }

    /// <summary>
    /// This window's DIP-to-pixel factor, for the display it is on right now.
    /// </summary>
    /// <remarks>
    /// <c>XamlRoot</c> does not exist until the content loads — measured: it is still null
    /// immediately after <c>InitializeComponent</c>, which is where the constructor's sizing
    /// runs — so those calls fall through to the OS. Everything after load gets the live value,
    /// which follows the window across displays.
    /// </remarks>
    private double CurrentScale =>
        RootGrid.XamlRoot?.RasterizationScale ?? DisplayScale.ForWindow(AppWindow);

    /// <summary>
    /// Re-derives the resize floor for the display the window is on.
    /// </summary>
    /// <remarks>
    /// Re-derived rather than set once, because <c>PreferredMinimum*</c> is stored in physical
    /// pixels and Windows never rescales it. Measured: a window opened at 150% keeps a
    /// 1080×720px floor, and on a 100% display that reads as 1080×720 DIP — half again the
    /// intended 720×480, on a screen where it is over half the work area. The reverse direction
    /// drops the floor to 480×320 DIP, under the width the title bar row is laid out for.
    ///
    /// No explicit resize follows. The floor is a constraint, not a size, and pulling a window
    /// to a new size under the user is worse than leaving it where they put it — the window is
    /// already being rescaled by the DPI ratio around this call, which keeps the two in step.
    /// </remarks>
    private void ApplyMinimumSize(double scale)
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter || scale <= 0) return;

        var minimum = WindowPlacement.MinimumSize(scale);
        presenter.PreferredMinimumWidth = minimum.Width;
        presenter.PreferredMinimumHeight = minimum.Height;
    }

    /// <summary>
    /// Keeps the floor in step with the display, via <c>WM_DPICHANGED</c> rather than
    /// <c>XamlRoot.Changed</c> — see <see cref="DpiChangeWatcher"/> for why the obvious one is
    /// too late. Never detached: the main HWND outlives every close, since closing to the tray
    /// hides rather than destroys.
    /// </summary>
    private void InitializeMinimumSizeTracking() =>
        DpiChangeWatcher.Attach(WindowNative.GetWindowHandle(this), ApplyMinimumSize);

    /// <summary>
    /// Sizes the window in the constructor, before it is shown. The persisted size used
    /// to be applied only after the first workspace finished loading, which produced a
    /// visible resize pop on every launch.
    /// </summary>
    private void ApplyInitialWindowSize()
    {
        ApplyMinimumSize(CurrentScale);

        // Read config directly rather than waiting for InitializeWorkspacesAsync; this is
        // a small synchronous file read and it has to happen before the window is shown.
        var saved = DataService.LoadConfig().WindowState;

        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var size = WindowPlacement.ResolveStartupSize(
            saved, CurrentScale, new PixelSize(workArea.Width, workArea.Height));

        AppWindow.Resize(new SizeInt32(size.Width, size.Height));
    }

    #endregion

    private void UpdateUngroupedCount() =>
        UngroupedItemCount.Text = _ungroupedApps.Count.ToString();

    // FindDescendant / FindParent moved to Helpers/VisualTree.cs — the manage dialogs need
    // FindDescendant too, to reach the name box inside a freshly realized row. The partials
    // that call them carry a `using static ace_run.Helpers.VisualTree;`.
}
