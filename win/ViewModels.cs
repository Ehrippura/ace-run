using System;
using System.Collections.Generic;
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
    private readonly List<Guid> _tagIds = new();
    private string? _tagColorKey;
    private string? _tagName;

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

    /// <summary>
    /// Assigned tag ids. The data model keeps a list for future multi-tag support;
    /// V1 UI assigns at most one tag (a single-element list) or none (empty).
    /// </summary>
    public IReadOnlyList<Guid> TagIds => _tagIds;

    /// <summary>Replaces the assigned tag with a single tag, or clears it when null.</summary>
    public void SetSingleTag(Guid? tagId)
    {
        _tagIds.Clear();
        if (tagId is Guid id)
            _tagIds.Add(id);
    }

    /// <summary>
    /// Display color key resolved from the assigned tag (set by MainWindow).
    /// Drives the tag dot; null hides it.
    /// </summary>
    public string? TagColorKey
    {
        get => _tagColorKey;
        set
        {
            if (_tagColorKey != value)
            {
                _tagColorKey = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TagBrush));
                OnPropertyChanged(nameof(TagVisibility));
            }
        }
    }

    public string? TagName
    {
        get => _tagName;
        set { if (_tagName != value) { _tagName = value; OnPropertyChanged(); } }
    }

    public Brush TagBrush => ColorTags.GetBrush(_tagColorKey);

    public Visibility TagVisibility =>
        _tagColorKey is not null ? Visibility.Visible : Visibility.Collapsed;

    public AppItemViewModel(AppItem model)
    {
        Id = model.Id;
        _displayName = model.DisplayName;
        _filePath = model.FilePath;
        _arguments = model.Arguments;
        _workingDirectory = model.WorkingDirectory;
        _runAsAdmin = model.RunAsAdmin;
        _customIconPath = model.CustomIconPath;
        if (model.TagIds is not null)
            _tagIds.AddRange(model.TagIds);
    }

    public async Task LoadIconAsync()
    {
        IconSource = await IconService.GetIconAsync(FilePath, Id, _customIconPath);
    }

    public void ReleaseIcon() => IconSource = null;

    public AppItem ToModel() => new AppItem
    {
        Id = Id,
        DisplayName = DisplayName,
        FilePath = FilePath,
        Arguments = Arguments,
        WorkingDirectory = WorkingDirectory,
        RunAsAdmin = RunAsAdmin,
        CustomIconPath = CustomIconPath,
        TagIds = new List<Guid>(_tagIds)
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

    public Brush ColorBrush => ColorTags.GetBrush(_info.ColorTag);

    public Visibility HasColorVisibility =>
        _info.ColorTag is not null ? Visibility.Visible : Visibility.Collapsed;

    public string AppCountText =>
        string.Format(Loc.GetString("Workspace_AppCount"), _info.AppCount);

    public string ExportTooltip => Loc.GetString("Workspace_Export");
    public string DeleteTooltip => Loc.GetString("Workspace_Delete");

    public WorkspaceInfo ToInfo() => _info;

    public WorkspaceViewModel(WorkspaceInfo info)
    {
        _info = info;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class TagViewModel : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _colorKey = "Blue";

    public Guid Id { get; }

    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropertyChanged(); } }
    }

    public string ColorKey
    {
        get => _colorKey;
        set
        {
            if (_colorKey != value)
            {
                _colorKey = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ColorBrush));
            }
        }
    }

    public Brush ColorBrush => ColorTags.GetBrush(_colorKey);

    public string DeleteTooltip => Loc.GetString("Tag_Delete");

    public TagViewModel(TagItem model)
    {
        Id = model.Id;
        _name = model.Name;
        _colorKey = string.IsNullOrEmpty(model.ColorKey) ? "Blue" : model.ColorKey;
    }

    public TagViewModel(string name, string colorKey)
    {
        Id = Guid.NewGuid();
        _name = name;
        _colorKey = colorKey;
    }

    public TagItem ToModel() => new TagItem
    {
        Id = Id,
        Name = Name,
        ColorKey = ColorKey
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
