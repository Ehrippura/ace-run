using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;
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

public class WorkspaceViewModel : INotifyPropertyChanged
{
    private readonly WorkspaceInfo _info;
    private bool _isDefault;

    public Guid Id => _info.Id;

    public string Name
    {
        get => _info.Name;
        set
        {
            if (_info.Name != value)
            {
                _info.Name = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AppCountText));
            }
        }
    }

    public string? ColorTag
    {
        get => _info.ColorTag;
        set
        {
            if (_info.ColorTag != value)
            {
                _info.ColorTag = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ColorBrush));
                OnPropertyChanged(nameof(HasColorVisibility));
            }
        }
    }

    public int AppCount
    {
        get => _info.AppCount;
        set
        {
            if (_info.AppCount != value)
            {
                _info.AppCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AppCountText));
            }
        }
    }

    public bool IsDefault
    {
        get => _isDefault;
        set
        {
            if (_isDefault != value)
            {
                _isDefault = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DefaultGlyph));
            }
        }
    }

    public Brush ColorBrush => _info.ColorTag switch
    {
        "Blue"   => new SolidColorBrush(Color.FromArgb(255, 0, 120, 212)),
        "Green"  => new SolidColorBrush(Color.FromArgb(255, 16, 124, 16)),
        "Red"    => new SolidColorBrush(Color.FromArgb(255, 196, 43, 28)),
        "Yellow" => new SolidColorBrush(Color.FromArgb(255, 247, 99, 12)),
        "Purple" => new SolidColorBrush(Color.FromArgb(255, 136, 23, 152)),
        _        => new SolidColorBrush(Colors.Transparent)
    };

    public Visibility HasColorVisibility =>
        _info.ColorTag is not null ? Visibility.Visible : Visibility.Collapsed;

    public string AppCountText =>
        string.Format(Loc.GetString("Workspace_AppCount"), _info.AppCount);

    public string DefaultGlyph => _isDefault ? "\uE735" : "\uE734";

    public string SetDefaultTooltip => Loc.GetString("Workspace_SetDefault");
    public string ExportTooltip => Loc.GetString("Workspace_Export");
    public string DeleteTooltip => Loc.GetString("Workspace_Delete");

    public WorkspaceInfo ToInfo() => _info;

    public WorkspaceViewModel(WorkspaceInfo info, bool isDefault)
    {
        _info = info;
        _isDefault = isDefault;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
