using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using ace_run.Models;
using ace_run.Services;

namespace ace_run;

public abstract class TreeItemViewModel : INotifyPropertyChanged
{
    private string _displayName = string.Empty;

    public Guid Id { get; }

    public string DisplayName
    {
        get => _displayName;
        set { if (_displayName != value) { _displayName = value; OnPropertyChanged(); } }
    }

    protected TreeItemViewModel(Guid id, string displayName)
    {
        Id = id;
        _displayName = displayName;
    }

    public abstract TreeItem ToModel();

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class AppItemViewModel : TreeItemViewModel
{
    private string _filePath = string.Empty;
    private string _arguments = string.Empty;
    private string _workingDirectory = string.Empty;
    private bool _runAsAdmin;
    private string _customIconPath = string.Empty;
    private BitmapImage? _iconSource;

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
        : base(model.Id, model.DisplayName)
    {
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

    public override TreeItem ToModel() => new AppItem
    {
        Id = Id,
        DisplayName = DisplayName,
        FilePath = FilePath,
        Arguments = Arguments,
        WorkingDirectory = WorkingDirectory,
        RunAsAdmin = RunAsAdmin,
        CustomIconPath = CustomIconPath
    };
}

public class FolderViewModel : TreeItemViewModel
{
    private bool _isExpanded;

    public ObservableCollection<TreeItemViewModel> Children { get; } = new();

    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded != value) { _isExpanded = value; OnPropertyChanged(); } }
    }

    public FolderViewModel(FolderItem model)
        : base(model.Id, model.DisplayName)
    {
        _isExpanded = model.IsExpanded;
    }

    public FolderViewModel(string name)
        : base(Guid.NewGuid(), name)
    {
        _isExpanded = true;
    }

    public override TreeItem ToModel()
    {
        var folder = new FolderItem
        {
            Id = Id,
            DisplayName = DisplayName,
            IsExpanded = IsExpanded
        };
        foreach (var child in Children)
            folder.Children.Add(child.ToModel());
        return folder;
    }
}
