using System;
using System.Collections.ObjectModel;
using System.Linq;
using ace_run.Helpers;
using ace_run.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace ace_run;

public sealed partial class ManageTagsDialog : ContentDialog
{
    /// <summary>
    /// MainWindow's live collection, by reference. Mutating it here is the point — the tiles
    /// bind these very instances — and <c>ManageTagsButton_Click</c>'s <c>CommitSave()</c>
    /// after the dialog closes is what persists every change, order included. Never rebuild
    /// this collection: replacing the instances would strip the tags off every item.
    /// </summary>
    private readonly ObservableCollection<TagViewModel> _tags;

    /// <summary>
    /// The row being renamed, captured when its box takes focus rather than read back from
    /// <c>DataContext</c> at commit time. See <see cref="CommitName"/>.
    /// </summary>
    private TagViewModel? _editing;
    private string _editingOriginal = string.Empty;

    // Constant row labels. Static so the DataTemplate can x:Bind them without every view model
    // carrying a copy of the same string.
    public static string NameFieldLabel => Loc.GetString("Tag_Name");
    public static string MoreLabel => Loc.GetString("Row_More");
    public static string ReorderLabel => Loc.GetString("Row_Reorder");
    public static string ChooseColorLabel => Loc.GetString("Color_Choose");

    public ManageTagsDialog(ObservableCollection<TagViewModel> tags)
    {
        _tags = tags;

        InitializeComponent();

        Title = Loc.GetString("Tag_ManageTitle");
        PrimaryButtonText = Loc.GetString("CloseButton");
        DefaultButton = ContentDialogButton.Primary;

        NewTagLabel.Text = Loc.GetString("Tag_New");
        EmptyHint.Text = Loc.GetString("Tag_Empty");

        TagListView.ItemsSource = _tags;
        UpdateEmptyState();
    }

    // ---- New tag ----

    /// <remarks>
    /// The row is created immediately and the name box is focused, rather than opening a form
    /// to fill in first. The form it replaces was the source of three separate problems: Enter
    /// inside it reached the dialog's default button and closed the whole dialog, its only
    /// label was a placeholder, and it appeared below a list up to 360 DIP tall while the
    /// button that opened it stayed at the top.
    ///
    /// The trade is that an untouched row is real: close the dialog without typing and a tag
    /// called "Untitled Tag" is saved, where Cancel used to discard it. It is visible and one
    /// menu click from gone, which is the same bargain Windows' own list editors make.
    /// </remarks>
    private void NewTagButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorBar.IsOpen = false;

        var vm = new TagViewModel(UniqueName(Loc.GetString("Tag_DefaultName")), ColorKeys.Default);
        _tags.Add(vm);
        UpdateEmptyState();
        FocusNameBox(vm);
    }

    /// <summary>
    /// The given name, or the first free "<c>name N</c>" after it.
    /// </summary>
    /// <remarks>
    /// Without this, clicking New Tag twice would produce two rows with the default name and
    /// the second would trip the dialog's own duplicate check on its way in.
    /// </remarks>
    private string UniqueName(string basis)
    {
        if (!IsTaken(basis, null)) return basis;

        for (var n = 2; ; n++)
        {
            var candidate = $"{basis} {n}";
            if (!IsTaken(candidate, null)) return candidate;
        }
    }

    private bool IsTaken(string name, TagViewModel? exclude) =>
        _tags.Any(t => !ReferenceEquals(t, exclude)
                       && string.Equals(t.Name, name, StringComparison.CurrentCultureIgnoreCase));

    /// <summary>
    /// Focuses a row's name box, retrying once on the dispatcher.
    /// </summary>
    /// <remarks>
    /// <c>ContainerFromItem</c> answers null straight after an <c>Add</c> — layout has not run
    /// — so <c>UpdateLayout</c> covers the common case and the deferred retry covers the rest.
    /// </remarks>
    private void FocusNameBox(TagViewModel vm)
    {
        TagListView.ScrollIntoView(vm);
        TagListView.UpdateLayout();

        if (!TryFocusNameBox(vm))
            DispatcherQueue.TryEnqueue(() => TryFocusNameBox(vm));
    }

    private bool TryFocusNameBox(TagViewModel vm)
    {
        if (TagListView.ContainerFromItem(vm) is not DependencyObject container) return false;
        if (VisualTree.FindDescendant<TextBox>(container) is not { } box) return false;

        box.Focus(FocusState.Programmatic);
        box.SelectAll();
        return true;
    }

    /// <summary>
    /// Names the row container for a screen reader. Without it a row announces itself as
    /// "ace_run.TagViewModel" — see <see cref="ItemContainers.BindAutomationName"/>.
    /// </summary>
    private void TagListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not TagViewModel vm) return;
        ItemContainers.BindAutomationName(args.ItemContainer, vm, nameof(TagViewModel.Name));
    }

    // ---- Colour ----

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TagViewModel vm } button) return;

        // allowNone: false — a tag has no colourless state. TagViewModel coerces an empty key
        // to ColorKeys.Default, so offering "no colour" would show something that cannot stick.
        ColorSwatchFlyout.Show(button, vm.ColorKey, allowNone: false,
            key => vm.ColorKey = key ?? ColorKeys.Default);
    }

    // ---- Overflow menu ----

    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TagViewModel vm } button) return;

        var index = _tags.IndexOf(vm);

        ManageRowMenu.Show(
            button,
            onExport: null,
            onMoveUp: () => ItemOrdering.MoveBy(_tags, vm, -1),
            onMoveDown: () => ItemOrdering.MoveBy(_tags, vm, 1),
            canMoveUp: index > 0,
            canMoveDown: index >= 0 && index < _tags.Count - 1,
            onDelete: () => ManageRowMenu.ConfirmDelete(
                button,
                Loc.GetString("Tag_DeleteTitle"),
                string.Format(Loc.GetString("Tag_DeleteConfirm"), vm.Name),
                () =>
                {
                    _tags.Remove(vm);
                    UpdateEmptyState();
                }));
    }

    // ---- Inline rename ----

    private void TagName_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb) return;

        _editing = tb.DataContext as TagViewModel;
        _editingOriginal = _editing?.Name ?? string.Empty;
    }

    private void TagName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb) return;

        var vm = _editing;
        _editing = null;
        CommitName(tb, vm);
    }

    private void TagName_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox tb) return;

        // Marking these handled matters more than what they do. A single-line TextBox passes
        // Enter and Escape straight up, and this box lives inside a ContentDialog: Enter
        // reached DefaultButton — which is Close — so finishing a rename the obvious way shut
        // the dialog, and Escape did the same through the dialog's own cancel path.
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            CommitName(tb, _editing);
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            tb.Text = _editingOriginal;
        }
    }

    /// <summary>
    /// Writes the edited text back, or explains why it will not.
    /// </summary>
    /// <remarks>
    /// <paramref name="vm"/> is the row captured at <c>GotFocus</c>, and the identity check
    /// below is why. Reading <c>tb.DataContext</c> here instead is a data-corruption path: a
    /// <c>ListView</c>'s drag reorder mutates its source with RemoveAt + Insert and a delete
    /// shifts every container below it, so a container can be recycled onto a different tag
    /// between the edit and the commit — and the edit would land on that one.
    /// </remarks>
    private void CommitName(TextBox tb, TagViewModel? vm)
    {
        if (vm is null || !_tags.Contains(vm) || !ReferenceEquals(tb.DataContext, vm)) return;

        var newName = tb.Text.Trim();
        if (newName == vm.Name) return;

        if (string.IsNullOrEmpty(newName))
        {
            // Put the old name back rather than just declining the edit — returning here left
            // the box blank while the tag kept its name, and the two disagreed until something
            // else happened to redraw the row.
            tb.Text = vm.Name;
            return;
        }

        if (IsTaken(newName, vm))
        {
            ShowError(string.Format(Loc.GetString("Tag_DuplicateName"), newName));
            tb.Text = vm.Name;
            return;
        }

        ErrorBar.IsOpen = false;
        vm.Name = newName;
        _editingOriginal = newName;
    }

    // ---- Helpers ----

    private void UpdateEmptyState()
    {
        // The list goes with the hint. Leaving an empty ListView behind read as a stray box
        // under a sentence.
        var empty = _tags.Count == 0;
        EmptyHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        TagListView.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowError(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }
}
