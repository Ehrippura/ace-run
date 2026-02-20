using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using ace_run.Models;
using ace_run.Services;

namespace ace_run;

public class AppItemViewModel : INotifyPropertyChanged
{
    private string _displayName = string.Empty;
    private string _filePath = string.Empty;
    private string _arguments = string.Empty;
    private string _workingDirectory = string.Empty;
    private bool _runAsAdmin;
    private string _customIconPath = string.Empty;
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

    public string CustomIconPath
    {
        get => _customIconPath;
        set
        {
            if (_customIconPath != value)
            {
                IconService.InvalidateCache(Id);
                _customIconPath = value;
                OnPropertyChanged();
            }
        }
    }

    public BitmapImage? IconSource
    {
        get => _iconSource;
        private set { _iconSource = value; OnPropertyChanged(); }
    }

    public AppItemViewModel(AppItem model)
    {
        Id = model.Id;
        _displayName = model.DisplayName;
        _filePath = model.FilePath;
        _arguments = model.Arguments;
        _workingDirectory = model.WorkingDirectory;
        _runAsAdmin = model.RunAsAdmin;
        _customIconPath = model.CustomIconPath;
    }

    public async Task LoadIconAsync()
    {
        IconSource = await IconService.GetIconAsync(FilePath, Id, _customIconPath);
    }

    public AppItem ToModel() => new AppItem
    {
        Id = Id,
        DisplayName = DisplayName,
        FilePath = FilePath,
        Arguments = Arguments,
        WorkingDirectory = WorkingDirectory,
        RunAsAdmin = RunAsAdmin,
        CustomIconPath = CustomIconPath
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class FolderViewModel : INotifyPropertyChanged
{
    private string _displayName = string.Empty;

    public Guid Id { get; }

    public string DisplayName
    {
        get => _displayName;
        set { if (_displayName != value) { _displayName = value; OnPropertyChanged(); } }
    }

    public ObservableCollection<AppItemViewModel> Apps { get; } = new();

    public string IconGlyph => Id == Guid.Empty ? "\uE8FD" : "\uE8B7";

    public FolderViewModel(Guid id, string name)
    {
        Id = id;
        _displayName = name;
    }

    public FolderViewModel(FolderItem model)
    {
        Id = model.Id;
        _displayName = model.DisplayName;
    }

    public FolderViewModel(string name)
    {
        Id = Guid.NewGuid();
        _displayName = name;
    }

    public FolderItem ToModel()
    {
        var folder = new FolderItem
        {
            Id = Id,
            DisplayName = DisplayName
        };
        foreach (var app in Apps)
            folder.Children.Add(app.ToModel());
        return folder;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
