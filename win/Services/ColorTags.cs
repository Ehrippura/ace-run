using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ace_run.Services;

/// <summary>
/// Resolves a colour key to the theme-aware brush declared in <c>Styles/Brushes.xaml</c>.
/// The keys themselves are <see cref="ColorKeys"/>' — they are persisted to JSON and have no
/// business sitting next to a <see cref="SolidColorBrush"/>, whose static initializer used to
/// make the whole class unreachable without a running XAML application.
/// </summary>
internal static class ColorTags
{
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
