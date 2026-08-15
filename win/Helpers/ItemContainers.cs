using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Data;

namespace ace_run.Helpers;

/// <summary>
/// Helpers for the <c>ListViewItem</c> / <c>GridViewItem</c> a collection control generates,
/// as opposed to the template inside it.
/// </summary>
internal static class ItemContainers
{
    /// <summary>
    /// Gives an item container a real accessible name.
    /// </summary>
    /// <remarks>
    /// This has to happen on the container, not inside the item template: GridViewItem /
    /// ListViewItem derive from ContentControl, whose automation peer names itself from the
    /// *content's* plain text — for a templated item that is the view model's ToString(), so
    /// every row announced itself as "ace_run.AppItemViewModel". An AutomationProperties.Name
    /// set on the template root only names a child of the container, and Setter.Value in the
    /// ItemContainerStyle cannot carry a Binding in WinUI, so this is code.
    ///
    /// A binding rather than a plain string, so a rename under a live container (the edit
    /// dialog, a folder rename, an inline rename in the manage dialogs) reaches the announced
    /// name. The view model is passed as an explicit <see cref="Binding.Source"/>: an item
    /// container is a ContentControl whose Content is the item, but its DataContext is not, so
    /// a source-less binding here resolves against nothing. Recycling is covered by re-binding
    /// on every realization.
    /// </remarks>
    public static void BindAutomationName(DependencyObject container, object source, string path)
    {
        if (container is not FrameworkElement element) return;

        element.SetBinding(AutomationProperties.NameProperty, new Binding
        {
            Path = new PropertyPath(path),
            Source = source,
            Mode = BindingMode.OneWay
        });
    }
}
