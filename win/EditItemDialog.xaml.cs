using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using Windows.Storage.Pickers;
using ace_run.Services;
using WinRT.Interop;

namespace ace_run;

public sealed partial class EditItemDialog : ContentDialog
{
    private readonly AppItemViewModel _viewModel;
    private readonly IntPtr _hwnd;

    public EditItemDialog(AppItemViewModel viewModel, IntPtr hwnd)
        : this(viewModel, hwnd, Array.Empty<TagViewModel>())
    {
    }

    public EditItemDialog(AppItemViewModel viewModel, IntPtr hwnd, IReadOnlyList<TagViewModel> tags)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _hwnd = hwnd;

        PrimaryButtonText = Loc.GetString("SaveButton");
        CloseButtonText = Loc.GetString("CancelButton");

        DisplayNameBox.Text = viewModel.DisplayName;
        FilePathBox.Text = viewModel.FilePath;
        ArgumentsBox.Text = viewModel.Arguments;
        WorkingDirectoryBox.Text = viewModel.WorkingDirectory;
        RunAsAdminSwitch.IsOn = viewModel.RunAsAdmin;
        CustomIconPathBox.Text = viewModel.CustomIconPath;

        BuildTagItems(viewModel, tags);
    }

    private void BuildTagItems(AppItemViewModel viewModel, IReadOnlyList<TagViewModel> tags)
    {
        // First item: "No Tag" (Tag == null).
        TagCombo.Items.Add(new ComboBoxItem
        {
            Content = Loc.GetString("Tag_None"),
            Tag = null
        });

        var currentTagId = viewModel.TagIds.Count > 0 ? viewModel.TagIds[0] : (Guid?)null;
        int selectedIndex = 0;

        for (int i = 0; i < tags.Count; i++)
        {
            var tag = tags[i];
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            panel.Children.Add(new Ellipse
            {
                Width = 10,
                Height = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = ColorTags.GetBrush(tag.ColorKey)
            });
            panel.Children.Add(new TextBlock
            {
                Text = tag.Name,
                VerticalAlignment = VerticalAlignment.Center
            });

            TagCombo.Items.Add(new ComboBoxItem { Content = panel, Tag = tag.Id });

            if (currentTagId is Guid id && id == tag.Id)
                selectedIndex = i + 1; // +1 for the "No Tag" item
        }

        TagCombo.SelectedIndex = selectedIndex;
    }

    public void ApplyTo(AppItemViewModel viewModel)
    {
        viewModel.DisplayName = DisplayNameBox.Text;
        viewModel.FilePath = FilePathBox.Text;
        viewModel.Arguments = ArgumentsBox.Text;
        viewModel.WorkingDirectory = WorkingDirectoryBox.Text;
        viewModel.RunAsAdmin = RunAsAdminSwitch.IsOn;
        viewModel.CustomIconPath = CustomIconPathBox.Text;

        var selectedTagId = (TagCombo.SelectedItem as ComboBoxItem)?.Tag as Guid?;
        viewModel.SetSingleTag(selectedTagId);
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
