using System;
using System.Diagnostics;
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
        private TaskbarIcon? _trayIcon;

        public static bool TrayEnabled { get; private set; }

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();
            _window.Activate();
            _window.AttachContextMenus();

            InitializeTrayIcon();
        }

        private void InitializeTrayIcon()
        {
            try
            {
                _trayIcon = new TaskbarIcon();
                _trayIcon.ToolTipText = "Ace Run";

                var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.ico");
                _trayIcon.Icon = new System.Drawing.Icon(iconPath);

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

        private void UpdateTrayContextMenu()
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

            menu.Items.Add(new MenuFlyoutSeparator());

            // Recent launches
            var recents = _window.GetRecentLaunches();
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
                Command = new RelayCommand(ExitApp)
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
        }

        private void ExitApp()
        {
            TrayEnabled = false;
            _trayIcon?.Dispose();
            _trayIcon = null;
            _window?.Close();
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
