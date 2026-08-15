using System.Collections.Concurrent;
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
    /// One brush per (theme, key), so resolving stays allocation-free after the first call.
    /// The dictionaries never change at runtime, so an entry can never go stale.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Brush> Cache = new();

    /// <summary>
    /// Resolves a colour key to its brush, against the theme the app is actually wearing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not <c>Application.Current.Resources[key]</c>, which answers for the *app* theme —
    /// fixed at the system theme for the life of the process, because
    /// <c>Application.RequestedTheme</c> can only be set before the first window exists. That
    /// is the lookup that shipped an invisible caption glyph (see <c>ThemeService.Attach</c>),
    /// and it was wrong here too: with the OS in Dark and the app set to Light, a tag dot drew
    /// #62ABF5 while the colour picker beside it drew #0F6CBD — the user chose one blue from
    /// the palette and the row showed a different one.
    /// </para>
    /// <para>
    /// Resolution happens at call time and is not tracked afterwards. Callers that cache the
    /// result (the view models expose it as a property) must raise a change notification when
    /// the theme moves for the bindings to re-read.
    /// </para>
    /// </remarks>
    public static Brush GetBrush(string? colorKey) =>
        ResolveBrush(colorKey, ThemeService.ToElementTheme(App.CurrentTheme));

    /// <summary>
    /// Resolves a colour key against a specific <see cref="ElementTheme"/>, for a surface that
    /// may be wearing one of its own.
    /// </summary>
    /// <remarks>
    /// <see cref="ElementTheme.Default"/> and High Contrast both fall through to the
    /// app-resources lookup. For Default that is the right answer by definition. For High
    /// Contrast it is right because that dictionary is chosen system-wide rather than per
    /// element, so whenever it is active it wins at every level — the same reasoning
    /// <c>AceIsHighContrast</c> rests on in <c>Brushes.xaml</c>.
    /// </remarks>
    public static Brush ResolveBrush(string? colorKey, ElementTheme theme)
    {
        if (string.IsNullOrEmpty(colorKey)) return NoColorBrush;

        if (theme == ElementTheme.Default || ThemeService.IsHighContrast)
            return SharedBrush(colorKey);

        return Cache.GetOrAdd($"{theme}:{colorKey}", _ =>
        {
            var name = theme == ElementTheme.Dark ? "Dark" : "Light";

            foreach (var merged in Application.Current.Resources.MergedDictionaries)
            {
                if (merged.ThemeDictionaries.TryGetValue(name, out var entry)
                    && entry is ResourceDictionary themed
                    && themed.TryGetValue($"AceTagBrush{colorKey}", out var value)
                    && value is SolidColorBrush found)
                {
                    // A fresh brush, not the dictionary's own: that instance belongs to a
                    // theme the framework is not currently applying.
                    return new SolidColorBrush(found.Color);
                }
            }

            return SharedBrush(colorKey);
        });
    }

    /// <summary>The app-theme instance from application resources.</summary>
    private static Brush SharedBrush(string colorKey)
    {
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
