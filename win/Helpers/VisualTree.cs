using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ace_run.Helpers;

/// <summary>
/// The two visual-tree walks the app needs. They lived as private statics on
/// <c>MainWindow</c> until the manage dialogs needed <see cref="FindDescendant{T}"/> too — to
/// reach the name box inside a freshly realized <c>ListViewItem</c>.
/// </summary>
internal static class VisualTree
{
    /// <summary>
    /// First descendant of the given type, breadth-first-ish. The counterpart to
    /// <see cref="FindParent{T}"/>, and the only way to reach a template part from outside
    /// the control — <c>GetTemplateChild</c> is protected and <c>FindName</c> does not cross
    /// into a template's namescope.
    /// </summary>
    public static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } nested) return nested;
        }
        return null;
    }

    public static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent is not null)
        {
            if (parent is T t) return t;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }
}
