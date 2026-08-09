using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ace_run.Services;

/// <summary>
/// Shared color palette for workspace color tags and app tags.
/// Keys are stable strings persisted to JSON; brushes resolve to theme-aware
/// resources declared in <c>Styles/Brushes.xaml</c>.
/// </summary>
internal static class ColorTags
{
    /// <summary>Selectable color keys, in display order.</summary>
    public static readonly IReadOnlyList<string> Keys = new[]
    {
        "Blue", "Green", "Red", "Yellow", "Purple", "Gray"
    };

    private static readonly SolidColorBrush NoColorBrush = new(Colors.Transparent);

    /// <summary>
    /// Resolves a color key to its brush. The returned brush is the shared instance
    /// from the app resource dictionary, so this is allocation-free per call — it used
    /// to allocate a new brush on every property get, once per row while scrolling.
    /// </summary>
    /// <remarks>
    /// Resource lookup resolves against the theme in effect *at call time*; it does not
    /// track later theme switches. Callers that cache the result (the view models expose
    /// it as a property) must raise a change notification on <c>ActualThemeChanged</c>
    /// so the bindings re-read.
    /// </remarks>
    public static Brush GetBrush(string? colorKey)
    {
        if (string.IsNullOrEmpty(colorKey))
            return NoColorBrush;

        var resources = Application.Current?.Resources;
        if (resources is not null
            && resources.TryGetValue($"AceTagBrush{colorKey}", out var value)
            && value is Brush brush)
        {
            return brush;
        }

        return NoColorBrush;
    }
}
