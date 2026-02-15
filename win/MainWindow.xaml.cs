using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
using ace_run.Models;
using ace_run.Services;

namespace ace_run;

public sealed partial class MainWindow : Window
{
    public ObservableCollection<AppItemViewModel> Items { get; } = new();
    public ObservableCollection<AppItemViewModel> FilteredItems { get; } = new();

    private string _searchText = string.Empty;

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
            var vm = new AppItemViewModel(item);
            Items.Add(vm);
            FilteredItems.Add(vm);
            _ = vm.LoadIconAsync();
        }
    }

    private void SaveItems()
    {
        DataService.Save(Items.Select(vm => vm.ToModel()));
    }

    private void ApplyFilter(string text)
    {
        _searchText = text;
        FilteredItems.Clear();
        foreach (var vm in Items)
        {
            if (string.IsNullOrEmpty(text) ||
                vm.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                FilteredItems.Add(vm);
            }
        }
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        ApplyFilter(sender.Text);
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

        await AddItemFromPathAsync(file.Path);
    }

    private async Task AddItemFromPathAsync(string filePath)
    {
        var item = new AppItem
        {
            DisplayName = Path.GetFileNameWithoutExtension(filePath),
            FilePath = filePath,
            WorkingDirectory = Path.GetDirectoryName(filePath) ?? string.Empty
        };

        var vm = new AppItemViewModel(item);
        var hwnd = WindowNative.GetWindowHandle(this);
        var dialog = new EditItemDialog(vm, hwnd);
        dialog.XamlRoot = Content.XamlRoot;
        dialog.Title = Loc.GetString("AddItemTitle");

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            dialog.ApplyTo(vm);
            Items.Add(vm);
            ApplyFilter(_searchText);
            _ = vm.LoadIconAsync();
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
                ApplyFilter(_searchText);
                _ = vm.LoadIconAsync();
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
                Title = Loc.GetString("DeleteItemTitle"),
                Content = string.Format(Loc.GetString("DeleteItemContent"), vm.DisplayName),
                PrimaryButtonText = Loc.GetString("DeleteButton"),
                CloseButtonText = Loc.GetString("CancelButton"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };

            if (await confirmDialog.ShowAsync() == ContentDialogResult.Primary)
            {
                Items.Remove(vm);
                FilteredItems.Remove(vm);
                SaveItems();
            }
        }
    }

    private void RootGrid_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = Loc.GetString("DragDropCaption");
            e.DragUIOverride.IsGlyphVisible = true;
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
    }

    private async void RootGrid_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            return;

        var deferral = e.GetDeferral();
        try
        {
            var storageItems = await e.DataView.GetStorageItemsAsync();
            foreach (var storageItem in storageItems.OfType<StorageFile>())
            {
                var filePath = storageItem.Path;

                if (storageItem.FileType.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    filePath = ResolveLnkTarget(storageItem.Path) ?? storageItem.Path;
                }

                if (filePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(filePath))
                {
                    await AddItemFromPathAsync(filePath);
                }
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private static string? ResolveLnkTarget(string lnkPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return null;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(lnkPath);
            return (string)shortcut.TargetPath;
        }
        catch
        {
            return null;
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
    private BitmapImage? _iconSource;

    public Guid Id { get; }

    public string DisplayName
    {
        get => _displayName;
        set { if (_displayName != value) { _displayName = value; OnPropertyChanged(); } }
    }

    public string FilePath
    {
        get => _filePath;
        set
        {
            if (_filePath != value)
            {
                if (_filePath.Length > 0)
                    IconService.InvalidateCache(Id);
                _filePath = value;
                OnPropertyChanged();
            }
        }
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

    public BitmapImage? IconSource
    {
        get => _iconSource;
        private set { _iconSource = value; OnPropertyChanged(); }
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

    public async Task LoadIconAsync()
    {
        IconSource = await IconService.GetIconAsync(FilePath, Id);
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
