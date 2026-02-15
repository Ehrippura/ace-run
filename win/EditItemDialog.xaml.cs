using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using ace_run.Services;
using WinRT.Interop;

namespace ace_run;

public sealed partial class EditItemDialog : ContentDialog
{
    private readonly AppItemViewModel _viewModel;
    private readonly IntPtr _hwnd;

    public EditItemDialog(AppItemViewModel viewModel, IntPtr hwnd)
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
    }

    public void ApplyTo(AppItemViewModel viewModel)
    {
        viewModel.DisplayName = DisplayNameBox.Text;
        viewModel.FilePath = FilePathBox.Text;
        viewModel.Arguments = ArgumentsBox.Text;
        viewModel.WorkingDirectory = WorkingDirectoryBox.Text;
        viewModel.RunAsAdmin = RunAsAdminSwitch.IsOn;
    }

    private async void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, _hwnd);
        picker.FileTypeFilter.Add(".exe");

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            FilePathBox.Text = file.Path;
        }
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
