using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using Windows.Storage.Pickers;
using ace_run.Services;
using WinRT.Interop;

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
    private List<TagViewModel> SelectedTags() =>
        _tags.Where(t => _selectedTagIds.Contains(t.Id)).ToList();

    public void ApplyTo(AppItemViewModel viewModel)
    {
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

        viewModel.SetTags(SelectedTags());
    }

    private async void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, _hwnd);
        picker.FileTypeFilter.Add(".exe");
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.SettingsIdentifier = "AceRunOpenFilePicker";

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            FilePathBox.Text = file.Path;
        }
    }

    private async void BrowseIcon_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, _hwnd);
        picker.FileTypeFilter.Add(".ico");
        picker.FileTypeFilter.Add(".exe");
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.SettingsIdentifier = "AceRunIconPicker";

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
            CustomIconPathBox.Text = file.Path;
    }

    private async void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        InitializeWithWindow.Initialize(picker, _hwnd);
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            WorkingDirectoryBox.Text = folder.Path;
        }
    }
}
