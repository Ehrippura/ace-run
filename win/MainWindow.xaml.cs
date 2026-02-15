using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;
using ace_run.Models;
using ace_run.Services;

namespace ace_run;

public sealed partial class MainWindow : Window
{
    public ObservableCollection<AppItemViewModel> Items { get; } = new();

    public MainWindow()
    {
        InitializeComponent();

        LoadItems();
        Closed += MainWindow_Closed;
    }

    private void LoadItems()
    {
        foreach (var item in DataService.Load())
        {
            Items.Add(new AppItemViewModel(item));
        }
    }

    private void SaveItems()
    {
        DataService.Save(Items.Select(vm => vm.ToModel()));
    }

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = WindowNative.GetWindowHandle(this);

        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add(".exe");

        var file = await picker.PickSingleFileAsync();
        if (file is null)
            return;

        var item = new AppItem
        {
            DisplayName = Path.GetFileNameWithoutExtension(file.Name),
            FilePath = file.Path,
            WorkingDirectory = Path.GetDirectoryName(file.Path) ?? string.Empty
        };

        var vm = new AppItemViewModel(item);

        var dialog = new EditItemDialog(vm, hwnd);
        dialog.XamlRoot = Content.XamlRoot;
        dialog.Title = "Add Item";

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            dialog.ApplyTo(vm);
            Items.Add(vm);
            SaveItems();
        }
    }

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Guid id)
        {
            var item = Items.FirstOrDefault(i => i.Id == id);
            if (item is null)
                return;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = item.FilePath,
                    Arguments = item.Arguments,
                    WorkingDirectory = item.WorkingDirectory,
                    UseShellExecute = true
                };
                if (item.RunAsAdmin)
                    psi.Verb = "runas";

                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to launch: {ex.Message}");
            }
        }
    }

    private async void EditItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem menuItem && menuItem.Tag is Guid id)
        {
            var vm = Items.FirstOrDefault(i => i.Id == id);
            if (vm is null)
                return;

            var hwnd = WindowNative.GetWindowHandle(this);
            var dialog = new EditItemDialog(vm, hwnd);
            dialog.XamlRoot = Content.XamlRoot;

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                dialog.ApplyTo(vm);
                SaveItems();
            }
        }
    }

    private async void DeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem menuItem && menuItem.Tag is Guid id)
        {
            var vm = Items.FirstOrDefault(i => i.Id == id);
            if (vm is null)
                return;

            var confirmDialog = new ContentDialog
            {
                Title = "Delete Item",
                Content = $"Are you sure you want to delete \"{vm.DisplayName}\"?",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };

            if (await confirmDialog.ShowAsync() == ContentDialogResult.Primary)
            {
                Items.Remove(vm);
                SaveItems();
            }
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        SaveItems();
    }
}

public class AppItemViewModel : INotifyPropertyChanged
{
    private string _displayName = string.Empty;
    private string _filePath = string.Empty;
    private string _arguments = string.Empty;
    private string _workingDirectory = string.Empty;
    private bool _runAsAdmin;

    public Guid Id { get; }

    public string DisplayName
    {
        get => _displayName;
        set { if (_displayName != value) { _displayName = value; OnPropertyChanged(); } }
    }

    public string FilePath
    {
        get => _filePath;
        set { if (_filePath != value) { _filePath = value; OnPropertyChanged(); } }
    }

    public string Arguments
    {
        get => _arguments;
        set { if (_arguments != value) { _arguments = value; OnPropertyChanged(); } }
    }

    public string WorkingDirectory
    {
        get => _workingDirectory;
        set { if (_workingDirectory != value) { _workingDirectory = value; OnPropertyChanged(); } }
    }

    public bool RunAsAdmin
    {
        get => _runAsAdmin;
        set { if (_runAsAdmin != value) { _runAsAdmin = value; OnPropertyChanged(); } }
    }

    public AppItemViewModel(AppItem model)
    {
        Id = model.Id;
        _filePath = model.FilePath;
        _displayName = model.DisplayName;
        _arguments = model.Arguments;
        _workingDirectory = model.WorkingDirectory;
        _runAsAdmin = model.RunAsAdmin;
    }

    public AppItem ToModel() => new()
    {
        Id = Id,
        DisplayName = DisplayName,
        FilePath = FilePath,
        Arguments = Arguments,
        WorkingDirectory = WorkingDirectory,
        RunAsAdmin = RunAsAdmin
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
