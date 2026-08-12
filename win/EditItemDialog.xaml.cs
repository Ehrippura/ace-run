using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using ace_run.Services;

namespace ace_run;

public sealed partial class EditItemDialog : ContentDialog
{
    /// <summary>Dots shown on the button face before the rest fold into a counter.</summary>
    private const int MaxSummaryDots = 3;

    private readonly AppItemViewModel _viewModel;
    private readonly IntPtr _hwnd;
    private readonly IReadOnlyList<TagViewModel> _tags;
    private readonly HashSet<Guid> _selectedTagIds = new();
    private bool _suppressTagSelection;

    public EditItemDialog(AppItemViewModel viewModel, IntPtr hwnd)
        : this(viewModel, hwnd, Array.Empty<TagViewModel>())
    {
    }

    public EditItemDialog(AppItemViewModel viewModel, IntPtr hwnd, IReadOnlyList<TagViewModel> tags)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _hwnd = hwnd;
        _tags = tags;

        PrimaryButtonText = Loc.GetString("SaveButton");
        CloseButtonText = Loc.GetString("CancelButton");

        DisplayNameBox.Text = viewModel.DisplayName;
        FilePathBox.Text = viewModel.FilePath;
        ArgumentsBox.Text = viewModel.Arguments;
        WorkingDirectoryBox.Text = viewModel.WorkingDirectory;
        RunAsAdminSwitch.IsOn = viewModel.RunAsAdmin;
        CustomIconPathBox.Text = viewModel.CustomIconPath;

        SortKeyBox.Header = Loc.GetString("SortKey_Field");
        SortKeyBox.PlaceholderText = Loc.GetString("SortKey_Placeholder");
        SortKeyBox.Text = viewModel.SortKey;

        BuildTagPicker(viewModel, tags);

        if (viewModel.IsUrl)
            ApplyUrlMode();
    }

    /// <summary>
    /// Relabels the path field as an address and hides the exe-only rows. x:Uid values are
    /// static, so the URL captions are applied here instead.
    /// </summary>
    private void ApplyUrlMode()
    {
        FilePathBox.Header = Loc.GetString("UrlFieldHeader");
        FilePathBox.PlaceholderText = Loc.GetString("UrlFieldPlaceholder");

        BrowseFileButton.Visibility = Visibility.Collapsed;
        ArgumentsBox.Visibility = Visibility.Collapsed;
        WorkingDirectoryRow.Visibility = Visibility.Collapsed;
        RunAsAdminSwitch.Visibility = Visibility.Collapsed;

        PrimaryButtonClick += UrlDialog_PrimaryButtonClick;
    }

    private void UrlDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (UrlUtil.TryNormalize(FilePathBox.Text, out _))
        {
            ValidationText.Visibility = Visibility.Collapsed;
            return;
        }

        ValidationText.Text = Loc.GetString("Validation_InvalidUrl");
        ValidationText.Visibility = Visibility.Visible;
        args.Cancel = true;
    }

    private void BuildTagPicker(AppItemViewModel viewModel, IReadOnlyList<TagViewModel> tags)
    {
        TagFieldLabel.Text = Loc.GetString("Tag_Field");

        foreach (var tag in viewModel.Tags)
            _selectedTagIds.Add(tag.Id);

        // Nothing to pick from: leave the button disabled rather than opening an empty list.
        TagDropDown.IsEnabled = tags.Count > 0;
        TagListView.ItemsSource = tags;

        UpdateTagSummary();
    }

    /// <summary>
    /// Restores the selection into the ListView. Writing <c>SelectedItems</c> before the
    /// containers exist doesn't stick, and the flyout only realizes the list when it first
    /// opens — so this runs on Loaded, which fires again on every reopen. Driving it from
    /// <see cref="_selectedTagIds"/> keeps it idempotent.
    /// </summary>
    private void TagListView_Loaded(object sender, RoutedEventArgs e)
    {
        _suppressTagSelection = true;
        TagListView.SelectedItems.Clear();
        foreach (var tag in _tags)
            if (_selectedTagIds.Contains(tag.Id))
                TagListView.SelectedItems.Add(tag);
        _suppressTagSelection = false;
    }

    private void TagListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTagSelection) return;

        _selectedTagIds.Clear();
        foreach (var item in TagListView.SelectedItems)
            if (item is TagViewModel tag)
                _selectedTagIds.Add(tag.Id);

        UpdateTagSummary();
    }

    /// <summary>Redraws the button face: a dot per selected tag plus their names.</summary>
    private void UpdateTagSummary()
    {
        TagSummaryDots.Children.Clear();

        if (_tags.Count == 0)
        {
            TagSummaryText.Text = Loc.GetString("Tag_Empty");
            return;
        }

        var selected = SelectedTags();
        if (selected.Count == 0)
        {
            TagSummaryText.Text = Loc.GetString("Tag_None");
            return;
        }

        foreach (var tag in selected.Take(MaxSummaryDots))
        {
            TagSummaryDots.Children.Add(new Ellipse
            {
                Width = 10,
                Height = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = tag.ColorBrush
            });
        }

        if (selected.Count > MaxSummaryDots)
        {
            TagSummaryDots.Children.Add(new TextBlock
            {
                Text = string.Format(Loc.GetString("Tag_Overflow"), selected.Count - MaxSummaryDots),
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        TagSummaryText.Text = string.Join(Loc.GetString("Tag_Separator"), selected.Select(t => t.Name));
    }

    /// <summary>Selected tags in workspace order, so assignment order is never user state.</summary>
    private List<TagViewModel> SelectedTags() => TagOrdering.InWorkspaceOrder(_tags, _selectedTagIds);

    public void ApplyTo(AppItemViewModel viewModel)
    {
        // The cached icon is keyed by item id and was extracted from one of these two paths,
        // so it outlives a change to either. Captured here and compared at the end rather than
        // invalidated from the property setters: a string assignment that deletes a file is a
        // side effect nobody reading the call site would expect, and it made the view model
        // untestable without a disk.
        var previousFilePath = viewModel.FilePath;
        var previousIconPath = viewModel.CustomIconPath;

        if (viewModel.IsUrl)
        {
            // PrimaryButtonClick already rejected anything TryNormalize can't handle.
            UrlUtil.TryNormalize(FilePathBox.Text, out var url);
            viewModel.FilePath = url;
            viewModel.DisplayName = string.IsNullOrWhiteSpace(DisplayNameBox.Text)
                ? UrlUtil.SuggestDisplayName(url)
                : DisplayNameBox.Text;
            // Arguments / WorkingDirectory / RunAsAdmin stay at their defaults for URLs.
        }
        else
        {
            viewModel.DisplayName = DisplayNameBox.Text;
            viewModel.FilePath = FilePathBox.Text;
            viewModel.Arguments = ArgumentsBox.Text;
            viewModel.WorkingDirectory = WorkingDirectoryBox.Text;
            viewModel.RunAsAdmin = RunAsAdminSwitch.IsOn;
        }

        viewModel.CustomIconPath = CustomIconPathBox.Text;

        // Trimmed, unlike the fields above: trailing whitespace would quietly change where
        // an item lands when organizing, and the user cannot see it in the box.
        viewModel.SortKey = SortKeyBox.Text.Trim();

        viewModel.SetTags(SelectedTags());

        // One call for both paths, and only when something actually moved. The old setters
        // invalidated per-property, and CustomIconPath did so without the "was there anything
        // there before" guard FilePath had — so setting a custom icon on a brand-new item sent
        // a delete for an id that had never been cached.
        if (viewModel.FilePath != previousFilePath || viewModel.CustomIconPath != previousIconPath)
            IconService.InvalidateCache(viewModel.Id);
    }

    // Stable per-call-site keys for the shell's "last folder used here" memory — what the old
    // pickers' SettingsIdentifier strings did. They only decide where the dialog opens when
    // there is no value in the box to open next to, so they must never be reused between the
    // three buttons.
    private static readonly Guid FilePickerClientId = new("6C0D8F2A-4E1B-4C67-9A2E-2F4B5A7C1D30");
    private static readonly Guid IconPickerClientId = new("6C0D8F2A-4E1B-4C67-9A2E-2F4B5A7C1D31");
    private static readonly Guid FolderPickerClientId = new("6C0D8F2A-4E1B-4C67-9A2E-2F4B5A7C1D32");

    private void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        var path = ShellFileDialog.PickFile(
            _hwnd,
            FilePickerClientId,
            PickerStart.FirstExisting(Directory.Exists, PickerStart.DirectoryOf(FilePathBox.Text)),
            ("*.exe", "*.exe"));

        if (path is not null)
        {
            FilePathBox.Text = path;
        }
    }

    private void BrowseIcon_Click(object sender, RoutedEventArgs e)
    {
        // Where the current icon lives, then the working directory, then where the app lives —
        // a first custom icon usually comes out of the program's own folder, and the working
        // directory is the user's own answer to "where this app's files are" when it differs
        // from the exe's folder. For a URL item that last candidate is an address, which
        // PickerStart drops.
        var start = PickerStart.FirstExisting(
            Directory.Exists,
            PickerStart.DirectoryOf(CustomIconPathBox.Text),
            WorkingDirectoryBox.Text,
            PickerStart.DirectoryOf(FilePathBox.Text));

        var path = ShellFileDialog.PickFile(
            _hwnd, IconPickerClientId, start, ("*.ico;*.exe", "*.ico;*.exe"));

        if (path is not null)
            CustomIconPathBox.Text = path;
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        // The box already holds a directory, so it is the candidate as-is. The exe's own folder
        // is the fallback because that is effectively what an empty working directory means for
        // the process being launched.
        var start = PickerStart.FirstExisting(
            Directory.Exists,
            WorkingDirectoryBox.Text,
            PickerStart.DirectoryOf(FilePathBox.Text));

        var path = ShellFileDialog.PickFolder(_hwnd, FolderPickerClientId, start);
        if (path is not null)
        {
            WorkingDirectoryBox.Text = path;
        }
    }
}
