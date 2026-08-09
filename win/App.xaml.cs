using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using H.NotifyIcon;
using ace_run.Models;
using ace_run.Services;

namespace ace_run
{
    public partial class App : Application
    {
        private MainWindow? _window;
        private SettingsWindow? _settingsWindow;
        private TaskbarIcon? _trayIcon;

        /// <summary>
        /// Whether the tray icon was actually created. Deliberately *not* the user's
        /// close-to-tray preference — that lives in <see cref="AppSettings.CloseToTray"/>.
        /// Hiding the window with no tray icon would leave no way back.
        /// </summary>
        public static bool TrayEnabled { get; private set; }

        /// <summary>The theme every new surface (settings window, dialogs) has to be told about.</summary>
        public static AppTheme CurrentTheme { get; private set; } = AppTheme.System;

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // Before anything can call Loc.GetString — MainWindow's constructor does, and
            // the language override cannot be applied retroactively. This is a second
            // synchronous read of config.json (ApplyInitialWindowSize does the first); the
            // file is small and both reads have to happen before the window is shown.
            Loc.Initialize(DataService.LoadConfig().Settings.Language);

            _window = new MainWindow();

            // Started from the Run key: stay in the tray. Showing the window at sign-in
            // would make "start with Windows" unusable for what it exists for.
            if (!StartedMinimized())
                _window.Activate();

            _window.AttachContextMenus();

            InitializeTrayIcon();
        }

        private static bool StartedMinimized()
        {
            foreach (var arg in Environment.GetCommandLineArgs())
                if (string.Equals(arg, StartupService.TrayArgument, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private void InitializeTrayIcon()
        {
            try
            {
                _trayIcon = new TaskbarIcon();
                _trayIcon.ToolTipText = "Ace Run";

                using var stream = typeof(App).Assembly.GetManifestResourceStream("ace_run.Assets.app-icon.ico")!;
                _trayIcon.Icon = new System.Drawing.Icon(stream);

                _trayIcon.DoubleClickCommand = new RelayCommand(ShowWindow);
                _trayIcon.ContextMenuMode = ContextMenuMode.PopupMenu;

                TrayEnabled = true;
                UpdateTrayContextMenu();

                _trayIcon.ForceCreate();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Tray icon init failed: {ex.Message}");
                TrayEnabled = false;
            }
        }

        public void UpdateTrayContextMenu()
        {
            if (_trayIcon is null || _window is null)
                return;

            var menu = new MenuFlyout();

            // Show
            var showItem = new MenuFlyoutItem
            {
                Text = Loc.GetString("TrayShow"),
                Command = new RelayCommand(ShowWindow)
            };
            menu.Items.Add(showItem);

            // While the window is hidden this is the only way into the settings.
            var settingsItem = new MenuFlyoutItem
            {
                Text = Loc.GetString("Settings_Title"),
                Command = new RelayCommand(() =>
                {
                    ShowWindow();
                    if (_window is not null) ShowSettings(_window);
                })
            };
            menu.Items.Add(settingsItem);

            // Recent launches
            var recents = _window.GetRecentLaunches();
            if (recents.Count > 0)
            {
                var clearItem = new MenuFlyoutItem
                {
                    Text = Loc.GetString("TrayClearRecent"),
                    Command = new RelayCommand(() => _window?.ClearRecentLaunches())
                };
                menu.Items.Add(clearItem);
            }

            menu.Items.Add(new MenuFlyoutSeparator());

            if (recents.Count > 0)
            {
                foreach (var recent in recents)
                {
                    var filePath = recent.FilePath;
                    var recentItem = new MenuFlyoutItem
                    {
                        Text = recent.DisplayName,
                        Command = new RelayCommand(() =>
                        {
                            try
                            {
                                Process.Start(new ProcessStartInfo
                                {
                                    FileName = filePath,
                                    UseShellExecute = true
                                });
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Failed to launch recent: {ex.Message}");
                            }
                        })
                    };
                    menu.Items.Add(recentItem);
                }
                menu.Items.Add(new MenuFlyoutSeparator());
            }

            // Exit
            var exitItem = new MenuFlyoutItem
            {
                Text = Loc.GetString("TrayExit"),
                Command = new RelayCommand(() => ExitApp())
            };
            menu.Items.Add(exitItem);

            _trayIcon.ContextFlyout = menu;
        }

        private void ShowWindow()
        {
            if (_window is null) return;
            _window.AppWindow.Show();
            _window.Activate();
            UpdateTrayContextMenu();
            BringToForeground();
        }

        /// <summary>
        /// What the global hotkey does. A summon-only hotkey is a one-way door: pressing it
        /// again while looking at the window should put it away.
        /// </summary>
        public void ToggleWindow()
        {
            if (_window is null) return;

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
            if (_window.AppWindow.IsVisible && GetForegroundWindow() == hwnd)
            {
                _window.AppWindow.Hide();
                return;
            }

            ShowWindow();

            // Queued rather than called inline. BringToForeground does its own work on the
            // dispatcher, and focus set before the window is actually foreground does not
            // stick. The queue is FIFO, so this lands after it.
            //
            // Only the hotkey path does this. Clicking the tray icon is a deliberate,
            // mouse-in-hand act; being dropped into a text field there is not what was asked
            // for, and it would steal the caret from whatever the user clicks next.
            _window.DispatcherQueue.TryEnqueue(() => _window?.FocusSearchBox());
        }

        /// <summary>
        /// Fans the theme out to every root that carries its own copy. A ContentDialog is
        /// handled at its own creation site (MainWindow.ShowModalAsync) because it does not
        /// exist yet when this runs.
        /// </summary>
        public void ApplyTheme(AppTheme theme)
        {
            CurrentTheme = theme;
            ThemeService.Apply(_window?.Content as FrameworkElement, theme);
            ThemeService.Apply(_settingsWindow?.Content as FrameworkElement, theme);
        }

        /// <summary>One settings window at a time; a second invocation just brings it forward.</summary>
        public void ShowSettings(MainWindow owner)
        {
            if (_settingsWindow is not null)
            {
                _settingsWindow.Activate();
                return;
            }

            _settingsWindow = new SettingsWindow(owner);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Activate();
        }

        public void BringToForeground()
        {
            _window?.DispatcherQueue.TryEnqueue(() =>
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
                if (IsIconic(hwnd))
                    ShowWindowWin32(hwnd, SW_RESTORE);
                _window.AppWindow.Show();
                SetForegroundWindow(hwnd);
                _window.Activate();
            });
        }

        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll", EntryPoint = "ShowWindow")] private static extern bool ShowWindowWin32(IntPtr hWnd, int nCmdShow);
        private const int SW_RESTORE = 9;

        /// <summary>
        /// Public because the close button now reaches it: with close-to-tray switched off,
        /// letting the window close is not enough — the tray icon keeps the process alive.
        /// </summary>
        /// <param name="closeWindow">
        /// False when called from the window's own Closed handler — the window is already
        /// going away, and closing it again from inside its own event is reentrancy for no
        /// benefit. The tray path (where the window is merely hidden) still needs it.
        /// </param>
        public void ExitApp(bool closeWindow = true)
        {
            TrayEnabled = false;
            _trayIcon?.Dispose();
            _trayIcon = null;
            if (closeWindow) _window?.Close();
            Environment.Exit(0);
        }
    }

    internal class RelayCommand : ICommand
    {
        private readonly Action _execute;
        public RelayCommand(Action execute) => _execute = execute;
#pragma warning disable CS0067
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute();
    }
}
