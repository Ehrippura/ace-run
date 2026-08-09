using ace_run.Models;
using Microsoft.UI.Xaml;

namespace ace_run.Services;

/// <summary>
/// Maps the stored <see cref="AppTheme"/> onto WinUI's per-element theme.
///
/// <c>Application.RequestedTheme</c> is not usable here: it can only be set before the
/// first window exists and never again, so it cannot serve a toggle. Setting
/// <c>RequestedTheme</c> on each root element does apply live, and the Mica backdrop and
/// the system caption buttons both follow the root element's actual theme.
///
/// Each surface has to be told separately. A <c>ContentDialog</c> is hosted on the popup
/// layer rather than inside the element tree that carries the override, and a second
/// window has a root of its own.
/// </summary>
internal static class ThemeService
{
    public static ElementTheme ToElementTheme(AppTheme theme) => theme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default
    };

    public static void Apply(FrameworkElement? root, AppTheme theme)
    {
        if (root is null) return;
        root.RequestedTheme = ToElementTheme(theme);
    }
}
