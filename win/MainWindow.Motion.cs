using ace_run.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace ace_run;

/// <summary>
/// The app's entire motion budget: two moments and nothing else.
/// 1. Launch — the app's one verb, and the only place a real animation is spent.
///    Programs can take seconds to show a window; this confirms the click landed.
/// 2. Workspace switch — the edge crossfades and the content fades back in.
///
/// Hover and press are deliberately *not* here. The GridViewItem's ListViewItemPresenter
/// already paints those states, themed and High Contrast-correct, for free. A scale on
/// press would have to animate the template Border, which lives *inside* that presenter —
/// the fill would stay put while the content shrank inside it. Owning the whole visual
/// state machine to fix that is exactly the "custom ControlTemplate for a standard
/// control" anti-pattern, and it buys the least valuable of the moments.
///
/// Everything else in the app is instant, deliberately.
/// </summary>
public sealed partial class MainWindow
{
    // Storyboard cannot target a ScaleTransform instance directly — Storyboard.SetTarget
    // needs an element and a property path down to the animated value. Targeting the
    // transform object itself compiles and runs but silently animates nothing.
    private const string ScaleXPath = "(UIElement.RenderTransform).(ScaleTransform.ScaleX)";
    private const string ScaleYPath = "(UIElement.RenderTransform).(ScaleTransform.ScaleY)";

    private const double PulseFrom = 0.97;
    private static readonly TimeSpan PulseDuration = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan EdgeDuration = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan ContentFadeDuration = TimeSpan.FromMilliseconds(120);

    /// <summary>
    /// Honours Settings → Accessibility → Visual effects → Animation effects. When off,
    /// every animation below collapses to an immediate value assignment.
    /// </summary>
    private readonly bool _animationsEnabled = new UISettings().AnimationsEnabled;

    /// <summary>
    /// Dedicated brush for the edge. It must not be a resource brush from ColorTags —
    /// those instances are shared across every tag bar and dot in the app, and animating
    /// one would drag all of them along with it.
    /// </summary>
    private SolidColorBrush? _edgeBrush;

    #region Workspace edge

    /// <summary>
    /// Creates the edge brush and hands the same instance to every surface that shows
    /// "which workspace am I in": the window edge, the rail's selection indicator, the search
    /// results' selection indicator, and the grid's selected-card border. Called from the
    /// constructor, before items are realised, because ListViewItemPresenter resolves these
    /// keys when the template is applied. One shared instance means they all crossfade
    /// together for free, and none of them uses the system accent, which clashed with the edge.
    /// </summary>
    private void InitializeWorkspaceBrush()
    {
        _edgeBrush = new SolidColorBrush(ResolveEdgeColor(null));
        WorkspaceEdge.BorderBrush = _edgeBrush;
        UpdateWindowEdgeCorners();

        // Overriding the brushes the built-in ListViewItemPresenter already uses keeps
        // the platform's own visuals — geometry, states and animation are untouched.
        // SearchResultsView is in here because the top hit is pre-selected after every
        // search pass, which would otherwise put a system-accent bar on screen constantly.
        foreach (var list in new[] { SidebarListView, SearchResultsView })
            foreach (var key in new[]
                     {
                         "ListViewItemSelectionIndicatorBrush",
                         "ListViewItemSelectionIndicatorPointerOverBrush",
                         "ListViewItemSelectionIndicatorPressedBrush"
                     })
            {
                list.Resources[key] = _edgeBrush;
            }

        // Same idea for the tile grid: the selected card's border was the system accent
        // while the rail beside it was the workspace colour.
        foreach (var key in new[]
                 {
                     "GridViewItemSelectedBorderBrush",
                     "GridViewItemSelectedPointerOverBorderBrush",
                     "GridViewItemSelectedPressedBorderBrush"
                 })
        {
            AppGridView.Resources[key] = _edgeBrush;
        }
    }

    private Color ResolveEdgeColor(string? colorKey)
    {
        if (ColorTags.GetBrush(colorKey) is SolidColorBrush { Color.A: > 0 } brush)
            return brush.Color;

        return Application.Current.Resources["AceEdgeInactiveColor"] is Color inactive
            ? inactive
            : Microsoft.UI.Colors.Gray;
    }

    /// <summary>
    /// The radius DWM is rounding this window's corners at, in DIPs.
    ///
    /// There is no OS call that returns this. The only thing DWM exposes is
    /// <c>DWMWA_WINDOW_CORNER_PREFERENCE</c>, and reading it back yields the *preference*
    /// (<c>DWMWCP_DEFAULT</c> — "system decides") rather than a measurement, so it can say
    /// whether rounding was opted out of but never how much. The number therefore has to
    /// come from the design system, and WinUI publishes it: <c>OverlayCornerRadius</c> is
    /// the 8 DIP it gives flyouts, dialogs and window corners alike. Reading the resource
    /// rather than hardcoding 8 means this tracks the platform if the ramp ever moves.
    /// </summary>
    private static double WindowCornerRadius =>
        Application.Current.Resources.TryGetValue("OverlayCornerRadius", out var value)
        && value is CornerRadius radius
            ? radius.TopLeft
            : 8;

    /// <summary>
    /// Whether DWM is rounding the window at all right now. Two cases where it is not:
    /// Windows 10 never rounds (this app still supports 1809), and Windows 11 squares a
    /// window that is maximised or full-screen. Restored is the only rounded state, so
    /// this tests for it positively rather than listing the exceptions.
    ///
    /// <c>Environment.OSVersion</c> is safe here — since .NET 5 it goes through
    /// <c>RtlGetVersion</c> and reports the real build, not a manifest-shimmed one.
    /// </summary>
    private bool WindowIsRounded =>
        Environment.OSVersion.Version.Build >= 22000
        && AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored };

    /// <summary>
    /// Squares the edge off whenever the window itself is square. A rounded stroke inside
    /// a square window leaves four visible notches at the corners.
    /// </summary>
    private void UpdateWindowEdgeCorners() =>
        WorkspaceEdge.CornerRadius = new CornerRadius(WindowIsRounded ? WindowCornerRadius : 0);

    /// <summary>
    /// Repaints the edge for the active workspace, crossfading when animated.
    ///
    /// A workspace with no colour assigned shows no edge at all — the frame is the
    /// workspace's identity, and a grey ring on every uncoloured workspace would read as
    /// window decoration rather than as state. That is done by fading the Border's
    /// <c>Opacity</c>, not by pushing a transparent colour into the brush: the same brush
    /// instance also paints the rail's selection indicator and the selected tile's border,
    /// and those must stay visible. They keep <c>AceEdgeInactiveColor</c>.
    /// </summary>
    private void UpdateWorkspaceEdge()
    {
        // The brush instance is created once in the constructor and never replaced —
        // the rail's selection indicator holds the same reference.
        if (_edgeBrush is null) return;

        var target = ResolveEdgeColor(_currentWorkspace.ColorTag);
        var edgeOpacity = HasWorkspaceColor(_currentWorkspace.ColorTag) ? 1d : 0d;

        if (!_animationsEnabled)
        {
            _edgeBrush.Color = target;
            WorkspaceEdge.Opacity = edgeOpacity;
            return;
        }

        var storyboard = new Storyboard();

        var recolor = new ColorAnimation
        {
            To = target,
            Duration = EdgeDuration,
            EnableDependentAnimation = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(recolor, _edgeBrush);
        Storyboard.SetTargetProperty(recolor, "Color");
        storyboard.Children.Add(recolor);

        var fade = new DoubleAnimation
        {
            To = edgeOpacity,
            Duration = EdgeDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fade, WorkspaceEdge);
        Storyboard.SetTargetProperty(fade, "Opacity");
        storyboard.Children.Add(fade);

        storyboard.Begin();
    }

    /// <summary>
    /// Whether the workspace has a real colour, as opposed to falling through to
    /// <c>ColorTags.NoColorBrush</c> — which is transparent, not absent, so the alpha
    /// channel is what actually distinguishes the two.
    /// </summary>
    private static bool HasWorkspaceColor(string? colorKey) =>
        ColorTags.GetBrush(colorKey) is SolidColorBrush { Color.A: > 0 };

    /// <summary>Fades the content surface back in after the workspace's items are swapped.</summary>
    private void FadeInContent()
    {
        if (!_animationsEnabled) return;

        var animation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = ContentFadeDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(animation, ContentSurface);
        Storyboard.SetTargetProperty(animation, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    #endregion

    #region Rail collapse

    // Below this the rail costs more than it gives: it would eat a large fraction of the
    // window and leave too little room for a usable tile grid.
    private const double RailCollapseWidthDip = 800;

    /// <summary>Shared by the title bar's pane toggle button and the Ctrl+B accelerator.</summary>
    private void ToggleRail() => RailSplitView.IsPaneOpen = !RailSplitView.IsPaneOpen;

    /// <summary>
    /// Switches the rail between inline and overlay. Driven from SizeChanged rather than
    /// an AdaptiveTrigger because this is a Window, not a Page, and a VisualStateManager
    /// on the root of a bare Window is unreliable.
    /// </summary>
    private void UpdateRailForWidth(double widthDip)
    {
        var narrow = widthDip < RailCollapseWidthDip;
        if (narrow == _railIsNarrow) return;
        _railIsNarrow = narrow;

        RailSplitView.DisplayMode = narrow ? SplitViewDisplayMode.Overlay : SplitViewDisplayMode.Inline;
        RailSplitView.IsPaneOpen = !narrow;
        // Overlay floats the pane above the content, so it needs an opaque background;
        // inline sits beside it and should let the Mica through.
        RailSplitView.PaneBackground = narrow
            ? (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"]
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    private bool _railIsNarrow;

    #endregion

    #region Launch pulse

    /// <summary>
    /// Brief 0.97 → 1.03 → 1.0 pulse on the launched tile. Fire-and-forget: it must not
    /// depend on whether Process.Start succeeds. Only the grid has a scale transform —
    /// pulsing a full-width search row would look wrong — so search launches are silent.
    /// </summary>
    private void PulseLaunch(AppItemViewModel app)
    {
        if (!_animationsEnabled) return;

        // Null for virtualised-out items, which is fine; there is nothing on screen to pulse.
        if (AppGridView.ContainerFromItem(app) is not ContentControl container) return;
        if (container.ContentTemplateRoot is not FrameworkElement root) return;
        if (root.RenderTransform is not ScaleTransform transform) return;

        var storyboard = new Storyboard();
        foreach (var path in new[] { ScaleXPath, ScaleYPath })
        {
            var animation = new DoubleAnimationUsingKeyFrames();
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame
            {
                KeyTime = TimeSpan.Zero,
                Value = PulseFrom
            });
            animation.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = TimeSpan.FromMilliseconds(80),
                Value = 1.03,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            animation.KeyFrames.Add(new EasingDoubleKeyFrame
            {
                KeyTime = PulseDuration,
                Value = 1.0,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
            Storyboard.SetTarget(animation, root);
            Storyboard.SetTargetProperty(animation, path);
            storyboard.Children.Add(animation);
        }
        storyboard.Begin();
    }

    #endregion
}
