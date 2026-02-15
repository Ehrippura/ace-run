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
        var picker = new FileOpenPicker();
        var hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add(".exe");

        var file = await picker.PickSingleFileAsync();
        if (file is null)
            return;

        var item = new AppItem
        {
            DisplayName = Path.GetFileNameWithoutExtension(file.Name),
            FilePath = file.Path
        };
        Items.Add(new AppItemViewModel(item));
        SaveItems();
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
                Process.Start(new ProcessStartInfo
                {
                    FileName = item.FilePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to launch: {ex.Message}");
            }
        }
    }

    private void DisplayName_LostFocus(object sender, RoutedEventArgs e)
    {
        SaveItems();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        SaveItems();
    }
}

public class AppItemViewModel : INotifyPropertyChanged
{
    private string _displayName = string.Empty;

    public Guid Id { get; }
    public string FilePath { get; }

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (_displayName != value)
            {
                _displayName = value;
                OnPropertyChanged();
            }
        }
    }

    public AppItemViewModel(AppItem model)
    {
        Id = model.Id;
        FilePath = model.FilePath;
        _displayName = model.DisplayName;
    }

    public AppItem ToModel() => new()
    {
        Id = Id,
        DisplayName = DisplayName,
        FilePath = FilePath
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
