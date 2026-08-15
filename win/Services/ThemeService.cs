using ace_run.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.UI;

namespace ace_run.Services;

/// <summary>
/// Maps the stored <see cref="AppTheme"/> onto WinUI's per-element theme.
///
/// <c>Application.RequestedTheme</c> is not usable here: it can only be set before the
/// first window exists and never again, so it cannot serve a toggle. Setting
/// <c>RequestedTheme</c> on each root element does apply live, and the Mica backdrop
/// follows the root element's actual theme.
///
/// Each surface has to be told separately. A <c>ContentDialog</c> is hosted on the popup
/// layer rather than inside the element tree that carries the override, and a second
/// window has a root of its own.
///
/// The caption buttons are the one thing that does *not* come along for free — see
/// <see cref="Attach"/>.
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

    /// <summary>
    /// Takes over the colouring of the window's caption buttons, and keeps it in step with
    /// the root element's theme from here on.
    ///
    /// The minimise / maximise / close glyphs are not in our visual tree — they belong to
    /// <c>AppWindow.TitleBar</c>. Setting <c>ExtendsContentIntoTitleBar</c> makes WinUI
    /// colour them for us from its own <c>WindowCaption*</c> resources, and that is where
    /// this goes wrong: it resolves them against the *app* theme, which is fixed at the
    /// system theme for the life of the process, and never re-reads the root element's
    /// <c>RequestedTheme</c>. With the OS in dark mode and the app set to Light the buttons
    /// therefore stayed dark-themed — a #FFFFFF glyph on a #F2F2F9 title bar, 1.06:1, all
    /// but invisible. The *inactive* glyph looked fine, which is what made the bug read as
    /// a focus problem: dark's disabled caption colour is #666666, and a mid grey happens
    /// to be legible on a light background as well. The reverse pairing (system light, app
    /// set to Dark) is the same bug mirrored.
    ///
    /// Driving this off <c>ActualThemeChanged</c> rather than off <see cref="Apply"/> is
    /// what makes one subscription cover both ways the theme can move: the user changing
    /// the app setting, and — while the setting is System — the user changing the OS theme
    /// under us. Applying once here as well is not redundant, because assigning a
    /// <c>RequestedTheme</c> the element already had raises no event.
    ///
    /// Call once per window, after its content exists.
    /// </summary>
    public static void Attach(Window window)
    {
        if (window.Content is not FrameworkElement root) return;

        root.ActualThemeChanged += (_, _) => ApplyCaptionColors(window);
        ApplyCaptionColors(window);
    }

    private static void ApplyCaptionColors(Window window)
    {
        if (window.Content is not FrameworkElement root) return;

        var bar = window.AppWindow.TitleBar;

        // High Contrast is the one case WinUI already gets right, because that dictionary
        // is chosen system-wide rather than per element — there is no app/element mismatch
        // to correct. Null hands each colour back rather than pinning our own over a
        // contrast theme the user chose for legibility.
        if (IsHighContrast)
        {
            bar.ButtonBackgroundColor = null;
            bar.ButtonInactiveBackgroundColor = null;
            bar.ButtonForegroundColor = null;
            bar.ButtonHoverForegroundColor = null;
            bar.ButtonPressedForegroundColor = null;
            bar.ButtonInactiveForegroundColor = null;
            bar.ButtonHoverBackgroundColor = null;
            bar.ButtonPressedBackgroundColor = null;
            return;
        }

        var theme = root.ActualTheme;
        if (Resolve("AceCaptionForegroundColor", theme) is not { } foreground) return;
        if (Resolve("AceCaptionForegroundInactiveColor", theme) is not { } inactive) return;
        if (Resolve("AceCaptionButtonHoverBackgroundColor", theme) is not { } hover) return;
        if (Resolve("AceCaptionButtonPressedBackgroundColor", theme) is not { } pressed) return;

        // Transparent, not the theme's surface colour: the row behind these buttons is
        // Mica, and an opaque fill would leave three squares of flat paint in it.
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;

        bar.ButtonForegroundColor = foreground;
        bar.ButtonHoverForegroundColor = foreground;
        bar.ButtonPressedForegroundColor = foreground;
        bar.ButtonInactiveForegroundColor = inactive;

        // Only the hover and pressed *fills* are ours. The close button's red is drawn by
        // the system on top of them and is deliberately left alone.
        bar.ButtonHoverBackgroundColor = hover;
        bar.ButtonPressedBackgroundColor = pressed;
    }

    /// <summary>
    /// Reads a <see cref="Color"/> out of a named theme dictionary directly.
    ///
    /// The obvious <c>Application.Current.Resources[key]</c> is exactly the lookup that
    /// caused the bug this method exists to fix — it resolves against the app theme, and
    /// the whole point here is to answer for the theme a particular window is wearing.
    /// There is no framework call that resolves a <c>ThemeResource</c> against an
    /// arbitrary <see cref="ElementTheme"/> from code, so the dictionary is indexed by
    /// name and the merged dictionaries are walked to find the one that carries the key.
    /// </summary>
    internal static Color? Resolve(string key, ElementTheme theme)
    {
        var name = theme == ElementTheme.Dark ? "Dark" : "Light";

        foreach (var merged in Application.Current.Resources.MergedDictionaries)
        {
            if (merged.ThemeDictionaries.TryGetValue(name, out var entry)
                && entry is ResourceDictionary themed
                && themed.TryGetValue(key, out var value)
                && value is Color color)
            {
                return color;
            }
        }

        return null;
    }

    internal static bool IsHighContrast =>
        Application.Current.Resources.TryGetValue("AceIsHighContrast", out var flag) && flag is true;

    /// <summary>
    /// Stamps the current app theme onto a flyout's presenter, so a popup built in code
    /// wears the same theme as the window that opened it.
    /// </summary>
    /// <remarks>
    /// Same problem as <c>ContentDialog</c>, one layer further out. A flyout is hosted on the
    /// popup root, not inside the element tree carrying <c>RequestedTheme</c>, so it does not
    /// reliably inherit from its placement target — which is why <c>ShowModalAsync</c> has to
    /// set <c>dialog.RequestedTheme</c> by hand in the first place. It has gone unnoticed
    /// because the app's only code-built flyout so far is <see cref="ConfirmFlyout"/>, whose
    /// content is one <c>TextBlock</c> and two default buttons — the least likely thing to
    /// reveal a mismatch. A grid of colour swatches is the most likely.
    ///
    /// The presenter style is built fresh rather than derived from the platform default:
    /// a <c>Style</c> may only set properties the default does not, and adding one
    /// <c>RequestedTheme</c> setter on top of an implicit lookup is exactly what
    /// <c>BasedOn</c> is for — but <c>FlyoutPresenterStyle</c> starts null, so there is
    /// nothing to base on and a bare setter is enough.
    /// </remarks>
    public static void ApplyTo(FlyoutBase flyout)
    {
        var theme = ToElementTheme(App.CurrentTheme);
        if (theme == ElementTheme.Default) return;

        var target = flyout is MenuFlyout ? typeof(MenuFlyoutPresenter) : typeof(FlyoutPresenter);
        var style = new Style(target);
        style.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty, theme));

        switch (flyout)
        {
            case MenuFlyout menu: menu.MenuFlyoutPresenterStyle = style; break;
            case Flyout plain: plain.FlyoutPresenterStyle = style; break;
        }
    }
}
