using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
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

public class AppItemViewModel : INotifyPropertyChanged, IAppItemView
{
    /// <summary>
    /// Satisfies <see cref="IAppItemView.Tags"/> without a projection: <see cref="Tags"/> is an
    /// <c>ObservableCollection&lt;TagViewModel&gt;</c> and <see cref="IEnumerable{T}"/> is
    /// covariant, so the same instance answers both. Explicit because the property types
    /// differ; the app keeps using the concrete <see cref="Tags"/>.
    /// </summary>
    IEnumerable<ITagRef> IAppItemView.Tags => Tags;

    private string _displayName = string.Empty;
    private string _filePath = string.Empty;
    private string _arguments = string.Empty;
    private string _workingDirectory = string.Empty;
    private bool _runAsAdmin;
    private string _customIconPath = string.Empty;
    private string _sortKey = string.Empty;
    private BitmapImage? _iconSource;
    private int _iconGeneration;
    private readonly ObservableCollection<TagViewModel> _tags = new();
    private string _folderLabel = string.Empty;

    public Guid Id { get; }

    /// <summary>
    /// Fixed at construction, like <see cref="Id"/>. An item never switches between an
    /// exe and a URL, so nothing downstream has to handle a kind change mid-edit.
    /// </summary>
    public ItemKind Kind { get; }

    public bool IsUrl => Kind == ItemKind.Url;

    public string DisplayName
    {
        get => _displayName;
        set { if (_displayName != value) { _displayName = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Changing this invalidates the item's cached icon, but the setter does not do it —
    /// <see cref="EditItemDialog.ApplyTo"/> is the only writer and owns that call. A property
    /// assignment that quietly deleted a file made the view model impossible to construct in a
    /// test without touching the disk, and made the side effect invisible at the call site.
    /// </summary>
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

    /// <inheritdoc cref="FilePath"/>
    public string CustomIconPath
    {
        get => _customIconPath;
        set { if (_customIconPath != value) { _customIconPath = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// User-defined ordering key, read only by Organize. Nothing binds to it — the edit
    /// dialog pushes and pulls it by hand like every other field there — but it notifies
    /// for consistency with the rest of the editable properties.
    /// </summary>
    public string SortKey
    {
        get => _sortKey;
        set { if (_sortKey != value) { _sortKey = value; OnPropertyChanged(); } }
    }

    public BitmapImage? IconSource
    {
        get => _iconSource;
        private set
        {
            _iconSource = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IconVisibility));
            OnPropertyChanged(nameof(FallbackIconVisibility));
        }
    }

    /// <summary>
    /// Segoe MDL2 glyph shown when no icon could be loaded: a globe for URLs, the default
    /// app glyph for an exe whose icon is missing (a dead path used to render as a blank cell).
    /// </summary>
    public string FallbackGlyph => IsUrl ? "" : "";

    public Visibility IconVisibility =>
        _iconSource is not null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility FallbackIconVisibility =>
        _iconSource is null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Assigned tags, holding the same <see cref="TagViewModel"/> instances as the
    /// workspace tag list. Sharing instances (rather than caching name/color here) is
    /// what makes a rename or recolor in the manage dialog reach every tile on its own.
    /// Order mirrors the workspace tag list; <c>NormalizeAppTags</c> maintains that.
    /// </summary>
    public ObservableCollection<TagViewModel> Tags => _tags;

    /// <summary>Replaces the assigned tags, in the order given.</summary>
    public void SetTags(IEnumerable<TagViewModel> tags)
    {
        foreach (var tag in _tags)
            tag.PropertyChanged -= OnTagPropertyChanged;
        _tags.Clear();
        foreach (var tag in tags)
        {
            tag.PropertyChanged += OnTagPropertyChanged;
            _tags.Add(tag);
        }
        OnTagsChanged();
    }

    // A rename has to reach TagsSummary, which is a flattened string rather than a
    // binding onto the tag itself. Color needs no such hop: the dots bind ColorBrush
    // on the TagViewModel directly.
    private void OnTagPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TagViewModel.Name))
            OnPropertyChanged(nameof(TagsSummary));
    }

    private void OnTagsChanged()
    {
        OnPropertyChanged(nameof(VisibleTags));
        OnPropertyChanged(nameof(OverflowLabel));
        OnPropertyChanged(nameof(OverflowVisibility));
        OnPropertyChanged(nameof(TagsVisibility));
        OnPropertyChanged(nameof(TagsSummary));
    }

    /// <summary>The tags that get a dot; the rest are folded into <see cref="OverflowLabel"/>.</summary>
    public IReadOnlyList<TagViewModel> VisibleTags => TagDisplay.Visible(_tags);

    public string OverflowLabel =>
        TagDisplay.OverflowCount(_tags) is var hidden && hidden > 0
            ? string.Format(Loc.GetString("Tag_Overflow"), hidden)
            : string.Empty;

    public Visibility OverflowVisibility =>
        TagDisplay.OverflowCount(_tags) > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility TagsVisibility =>
        _tags.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// All assigned tag names, flattened. Used as the automation name of the whole dot
    /// strip — naming each dot separately makes a screen reader read them one at a time.
    /// </summary>
    public string TagsSummary =>
        string.Join(Loc.GetString("Tag_Separator"), _tags.Select(t => t.Name));

    /// <summary>
    /// Name of the containing folder (or the "Ungrouped" label), shown in search
    /// results. Set by MainWindow when building the result list; unused elsewhere.
    /// </summary>
    public string FolderLabel
    {
        get => _folderLabel;
        set { if (_folderLabel != value) { _folderLabel = value; OnPropertyChanged(); } }
    }

    /// <param name="tags">
    /// The workspace tag list, used to resolve <see cref="AppItem.TagIds"/> into shared
    /// instances. Ids with no matching tag are dropped, and the assigned tags come out in
    /// workspace order so the dots read the same on every tile.
    /// </param>
    public AppItemViewModel(AppItem model, IReadOnlyList<TagViewModel> tags)
    {
        Id = model.Id;
        Kind = model.Kind;
        _displayName = model.DisplayName;
        _filePath = model.FilePath;
        _arguments = model.Arguments;
        _workingDirectory = model.WorkingDirectory;
        _runAsAdmin = model.RunAsAdmin;
        _customIconPath = model.CustomIconPath;
        _sortKey = model.SortKey;

        if (model.TagIds is { Count: > 0 })
            SetTags(TagOrdering.InWorkspaceOrder(tags, new HashSet<Guid>(model.TagIds)));
    }

    /// <summary>
    /// Loads the icon, discarding the result if anything changed the icon's state while the
    /// disk read was in flight.
    /// <para>
    /// The generation stamp is what makes a release actually stick. Without it a load that
    /// started before <see cref="ReleaseIcon"/> ran would come back afterwards and put the
    /// bitmap on a view model that nothing is showing — and, in the other direction, two
    /// overlapping loads could land out of order. Both counters only ever move on the UI
    /// thread, so no interlocking is needed.
    /// </para>
    /// </summary>
    public async Task LoadIconAsync()
    {
        var generation = ++_iconGeneration;
        var icon = await IconService.GetIconAsync(FilePath, Id, _customIconPath);
        if (generation != _iconGeneration) return;

        IconSource = icon;
    }

    public void ReleaseIcon()
    {
        _iconGeneration++;
        IconSource = null;
    }

    public AppItem ToModel() => new AppItem
    {
        Id = Id,
        Kind = Kind,
        DisplayName = DisplayName,
        FilePath = FilePath,
        Arguments = Arguments,
        WorkingDirectory = WorkingDirectory,
        RunAsAdmin = RunAsAdmin,
        CustomIconPath = CustomIconPath,
        TagIds = _tags.Select(t => t.Id).ToList(),
        SortKey = SortKey
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

    /// <summary>
    /// Item count shown at the right of the rail row. String-typed to match
    /// <see cref="WorkspaceViewModel.AppCountText"/>; x:Bind will not convert an int
    /// to TextBlock.Text on its own.
    /// </summary>
    public string AppCountText => Apps.Count.ToString();

    private bool _isDropTarget;

    /// <summary>
    /// True while a drag is hovering this row. Session-only and never persisted — it is not
    /// in <see cref="ToModel"/> and must not be: it describes the pointer, not the folder.
    ///
    /// The highlight is driven from the view model rather than by poking the container's
    /// Background, because the row's fills belong to ListViewItemPresenter and setting them
    /// by hand fights the platform's own rest / hover / selected states.
    /// </summary>
    public bool IsDropTarget
    {
        get => _isDropTarget;
        set
        {
            if (_isDropTarget == value) return;
            _isDropTarget = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DropTargetVisibility));
        }
    }

    /// <summary>Visibility-typed for x:Bind, matching the AppItemViewModel members.</summary>
    public Visibility DropTargetVisibility =>
        _isDropTarget ? Visibility.Visible : Visibility.Collapsed;

    public FolderViewModel(FolderItem model)
    {
        Id = model.Id;
        _displayName = model.DisplayName;
        TrackAppCount();
    }

    public FolderViewModel(string name)
    {
        Id = Guid.NewGuid();
        _displayName = name;
        TrackAppCount();
    }

    private void TrackAppCount() =>
        Apps.CollectionChanged += (_, _) => OnPropertyChanged(nameof(AppCountText));

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
    private bool _isActive;

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

    /// <summary>
    /// Marks the workspace open behind the manage dialog. Session-only and deliberately absent
    /// from <see cref="ToInfo"/> — same rule as <c>FolderViewModel.IsDropTarget</c>. The dialog
    /// builds its own view models from a freshly loaded config, so this cannot leak into the
    /// workspace picker's instances.
    /// </summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActiveVisibility));
            }
        }
    }

    public Visibility ActiveVisibility => _isActive ? Visibility.Visible : Visibility.Collapsed;

    public string InUseText => Loc.GetString("Workspace_InUse");

    public Brush ColorBrush => ColorTags.GetBrush(_info.ColorTag);

    public Visibility HasColorVisibility =>
        _info.ColorTag is not null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// The current colour in words, for the swatch button's tooltip — the button's face is a
    /// dot and nothing else. Null reads as "no colour", which is a real state here.
    /// </summary>
    public string ColorName =>
        Loc.GetString(_info.ColorTag is null ? "Color_None" : $"Color_{_info.ColorTag}");

    public string AppCountText =>
        string.Format(Loc.GetString("Workspace_AppCount"), _info.AppCount);

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

public class TagViewModel : INotifyPropertyChanged, ITagRef
{
    private string _name = string.Empty;
    private string _colorKey = ColorKeys.Default;

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
                OnPropertyChanged(nameof(ColorName));
            }
        }
    }

    public Brush ColorBrush => ColorTags.GetBrush(_colorKey);

    /// <summary>
    /// The current colour in words, for the swatch button's tooltip. The button's face is a
    /// dot and nothing else, so this is the only place the colour is named.
    /// </summary>
    public string ColorName => Loc.GetString($"Color_{_colorKey}");

    public TagViewModel(TagItem model)
    {
        Id = model.Id;
        _name = model.Name;
        _colorKey = string.IsNullOrEmpty(model.ColorKey) ? ColorKeys.Default : model.ColorKey;
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
