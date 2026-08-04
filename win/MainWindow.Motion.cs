using ace_run.Services;
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
/// 2. Workspace switch — the spine crossfades and the content fades back in.
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
    private static readonly TimeSpan SpineDuration = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan ContentFadeDuration = TimeSpan.FromMilliseconds(120);

    /// <summary>
    /// Honours Settings → Accessibility → Visual effects → Animation effects. When off,
    /// every animation below collapses to an immediate value assignment.
    /// </summary>
    private readonly bool _animationsEnabled = new UISettings().AnimationsEnabled;

    /// <summary>
    /// Dedicated brush for the spine. It must not be a resource brush from ColorTags —
    /// those instances are shared across every tag bar and dot in the app, and animating
    /// one would drag all of them along with it.
    /// </summary>
    private SolidColorBrush? _spineBrush;

    #region Workspace spine

    private Color ResolveSpineColor(string? colorKey)
    {
        if (ColorTags.GetBrush(colorKey) is SolidColorBrush { Color.A: > 0 } brush)
            return brush.Color;

        return Application.Current.Resources["AceSpineInactiveColor"] is Color inactive
            ? inactive
            : Microsoft.UI.Colors.Gray;
    }

    /// <summary>Repaints the spine for the active workspace, crossfading when animated.</summary>
    private void UpdateWorkspaceSpine()
    {
        var target = ResolveSpineColor(_currentWorkspace.ColorTag);

        if (_spineBrush is null)
        {
            _spineBrush = new SolidColorBrush(target);
            WorkspaceSpine.Background = _spineBrush;
            return;
        }

        if (!_animationsEnabled)
        {
            _spineBrush.Color = target;
            return;
        }

        var animation = new ColorAnimation
        {
            To = target,
            Duration = SpineDuration,
            EnableDependentAnimation = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(animation, _spineBrush);
        Storyboard.SetTargetProperty(animation, "Color");

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

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
