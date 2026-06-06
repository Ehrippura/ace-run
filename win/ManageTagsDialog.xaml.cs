using System.Collections.ObjectModel;
using ace_run.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace ace_run;

public sealed partial class ManageTagsDialog : ContentDialog
{
    private readonly ObservableCollection<TagViewModel> _tags;

    public ManageTagsDialog(ObservableCollection<TagViewModel> tags)
    {
        _tags = tags;

        InitializeComponent();

        Title = Loc.GetString("Tag_ManageTitle");
        PrimaryButtonText = Loc.GetString("CloseButton");
        DefaultButton = ContentDialogButton.Primary;

        NewTagLabel.Text = Loc.GetString("Tag_New");
        NewFormTitle.Text = Loc.GetString("Tag_NewTitle");
        NewNameBox.PlaceholderText = Loc.GetString("Tag_Name");
        ConfirmNewBtn.Content = Loc.GetString("SaveButton");
        CancelNewBtn.Content = Loc.GetString("CancelButton");
        EmptyHint.Text = Loc.GetString("Tag_Empty");

        PopulateColorCombo(NewColorCombo);

        TagListView.ItemsSource = _tags;
        UpdateEmptyState();
    }

    // ---- Color combo helpers ----

    private static void PopulateColorCombo(ComboBox combo)
    {
        if (combo.Items.Count > 0) return;

        foreach (var key in ColorTags.Keys)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            panel.Children.Add(new Ellipse
            {
                Width = 12,
                Height = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = ColorTags.GetBrush(key)
            });
            panel.Children.Add(new TextBlock
            {
                Text = key,
                VerticalAlignment = VerticalAlignment.Center
            });
            combo.Items.Add(new ComboBoxItem { Content = panel, Tag = key });
        }
    }

    private static void SelectColor(ComboBox combo, string? colorKey)
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem item && (item.Tag as string) == colorKey)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        combo.SelectedIndex = combo.Items.Count > 0 ? 0 : -1;
    }

    private void ColorCombo_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        PopulateColorCombo(combo);
        if (combo.DataContext is TagViewModel vm)
            SelectColor(combo, vm.ColorKey);
    }

    private void ColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        if (combo.DataContext is not TagViewModel vm) return;
        if ((combo.SelectedItem as ComboBoxItem)?.Tag is string key)
            vm.ColorKey = key;
    }

    // ---- New tag (inline form) ----

    private void NewTagButton_Click(object sender, RoutedEventArgs e)
    {
        NewNameBox.Text = string.Empty;
        NewColorCombo.SelectedIndex = 0;
        NewTagForm.Visibility = Visibility.Visible;
        NewNameBox.Focus(FocusState.Programmatic);
    }

    private void ConfirmNewTag_Click(object sender, RoutedEventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(NewNameBox.Text)
            ? Loc.GetString("Tag_New")
            : NewNameBox.Text.Trim();
        var colorKey = (NewColorCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Blue";

        _tags.Add(new TagViewModel(name, colorKey));
        NewTagForm.Visibility = Visibility.Collapsed;
        UpdateEmptyState();
    }

    private void CancelNewTag_Click(object sender, RoutedEventArgs e)
    {
        NewTagForm.Visibility = Visibility.Collapsed;
    }

    // ---- Inline rename ----

    private void TagName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (tb.DataContext is not TagViewModel vm) return;

        var newName = tb.Text.Trim();
        if (string.IsNullOrEmpty(newName))
        {
            tb.Text = vm.Name; // revert empty edits
            return;
        }
        vm.Name = newName;
    }

    // ---- Delete (with Flyout confirmation) ----

    private void DeleteTag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button deleteBtn || deleteBtn.Tag is not TagViewModel vm) return;

        var panel = new StackPanel { Spacing = 8, Padding = new Thickness(4) };
        panel.Children.Add(new TextBlock
        {
            Text = string.Format(Loc.GetString("Tag_DeleteConfirm"), vm.Name),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 220
        });

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        Flyout? flyout = null;

        var confirmBtn = new Button { Content = Loc.GetString("DeleteButton") };
        confirmBtn.Click += (_, _) =>
        {
            flyout?.Hide();
            _tags.Remove(vm);
            UpdateEmptyState();
        };

        var cancelBtn = new Button { Content = Loc.GetString("CancelButton") };
        cancelBtn.Click += (_, _) => flyout?.Hide();

        btnRow.Children.Add(confirmBtn);
        btnRow.Children.Add(cancelBtn);
        panel.Children.Add(btnRow);

        flyout = new Flyout { Content = panel };
        flyout.ShowAt(deleteBtn);
    }

    private void UpdateEmptyState()
    {
        EmptyHint.Visibility = _tags.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
