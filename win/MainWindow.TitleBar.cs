using System.Collections.Generic;
using ace_run.Services;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.Graphics;

namespace ace_run;

/// <summary>
/// The two jobs that came back to us when the chrome row stopped being a
/// <c>Microsoft.UI.Xaml.Controls.TitleBar</c> and became a plain <c>Grid</c>: reserving
/// the caption strip, and dimming when the window loses focus.
///
/// The reserve is why the swap happened at all. The SDK control puts
/// <c>AppWindow.TitleBar.RightInset</c> — a *physical pixel* count — straight into a
/// <c>ColumnDefinition.Width</c>, which is measured in DIPs. At 100% scaling the two
/// agree and nothing looks wrong; at 150% it reserved 216 DIP for a 144 DIP strip, and
/// the template adds a hard-coded 48 DIP spacer on top of that. The result was ~120 DIP
/// of dead title bar between the last button and the minimise box, reachable by no
/// public property. Here the reserve is <see cref="UpdateTitleBarInsets"/>, which is the
/// same arithmetic with the division actually performed.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// How far the chrome fades while the window is inactive. Roughly the ratio
    /// TextFillColorDisabled has to TextFillColorPrimary, which is what the SDK
    /// template's Deactivated visual states swapped in — approximated with opacity
    /// because the row is a dozen unrelated controls, not two TextBlocks.
    /// </summary>
    private const double TitleBarInactiveOpacity = 0.45;

    private void InitializeTitleBar()
    {
        // The row's own drag region. Interactive children inside it still get input;
        // that is how the search box worked under the SDK control too.
        SetTitleBar(AppTitleBar);

        // Neither button carries a label, so the tooltip is the whole affordance. Under
        // the SDK control the back button's tooltip was hardcoded English in the template
        // and Loc could not reach it — owning the button is what fixes that.
        var back = Loc.GetString("TitleBar_Back");
        ToolTipService.SetToolTip(BackButton, string.Format(Loc.GetString("Shortcut_Format"), back, "Alt+←"));
        AutomationProperties.SetName(BackButton, back);
        AutomationProperties.SetAcceleratorKey(BackButton, "Alt+Left");

        var toggle = Loc.GetString("TitleBar_ToggleRail");
        ToolTipService.SetToolTip(PaneToggleButton, string.Format(Loc.GetString("Shortcut_Format"), toggle, "Ctrl+B"));
        AutomationProperties.SetName(PaneToggleButton, toggle);
        AutomationProperties.SetAcceleratorKey(PaneToggleButton, "Ctrl+B");

        Activated += (_, e) => AppTitleBar.Opacity =
            e.WindowActivationState == WindowActivationState.Deactivated ? TitleBarInactiveOpacity : 1.0;

        // Insets are not known until the window has a XamlRoot to give us a scale.
        AppTitleBar.Loaded += (_, _) =>
        {
            // Fires on DPI change, which moves both the inset (physical) and the scale.
            AppTitleBar.XamlRoot.Changed += (_, _) => UpdateTitleBarInsets();
            UpdateTitleBarInsets();

            // Search is the row's one star column, so it absorbs every other change in the
            // row — including the inset columns being rewritten, which resizes nothing else
            // and so raises no other event. Its size pass is the reliable "something moved".
            // A pure sideways move (the field clamped at MaxWidth, sliding as the window
            // grows) does not raise it, which is what the RootGrid.SizeChanged call covers.
            SearchBox.SizeChanged += (_, _) => UpdateTitleBarPassthrough();
            UpdateTitleBarPassthrough();
        };
    }

    /// <summary>
    /// Cuts the row's own controls out of the drag region.
    ///
    /// <c>SetTitleBar(AppTitleBar)</c> registers exactly one Caption rect — the whole row —
    /// and no Passthrough rects at all, so every control in the row sits in *non-client*
    /// space. XAML still routes pointer input into them, which is why clicking and typing
    /// have always worked; but the cursor is picked by the non-client hit test, which knows
    /// nothing about the children, so <c>DefWindowProc</c> keeps answering HTCAPTION and the
    /// search box never shows an I-beam. Registering the children as Passthrough subtracts
    /// them from the caption rect, which is what makes the hit test — and therefore the
    /// cursor — land on the client area.
    ///
    /// Each control is listed individually rather than their parent panel, so the gaps
    /// between them stay draggable.
    /// </summary>
    private void UpdateTitleBarPassthrough()
    {
        var scale = AppTitleBar.XamlRoot?.RasterizationScale ?? 0;
        if (scale <= 0) return;

        var rects = new List<RectInt32>(6);
        foreach (var child in new FrameworkElement[]
                 { BackButton, PaneToggleButton, WorkspaceComboBox, SearchBox, AddButton, SettingsButton })
        {
            // Zero-sized until the first arrange pass; a 0x0 rect is harmless but pointless.
            if (child.ActualWidth <= 0 || child.ActualHeight <= 0) continue;

            var origin = child.TransformToVisual(Content).TransformPoint(new Point(0, 0));
            rects.Add(new RectInt32(
                (int)(origin.X * scale),
                (int)(origin.Y * scale),
                (int)(child.ActualWidth * scale),
                (int)(child.ActualHeight * scale)));
        }

        InputNonClientPointerSource.GetForWindowId(AppWindow.Id)
            .SetRegionRects(NonClientRegionKind.Passthrough, rects.ToArray());
    }

    /// <summary>
    /// Sizes the two inset columns and the row itself from <c>AppWindow.TitleBar</c>.
    /// Every value that API reports is in physical pixels and every value a
    /// <c>ColumnDefinition</c> holds is in DIPs, so the division is the whole method.
    ///
    /// Both sides are driven, not just the right: RTL layouts and left-handed caption
    /// buttons move the strip to <c>LeftInset</c>, and a hardcoded right-only reserve
    /// would then push the workspace picker under the close button.
    ///
    /// Called from <c>RootGrid.SizeChanged</c> as well as on DPI change, because
    /// maximising changes the caption height. It writes nothing when nothing moved, so
    /// the size pass it runs inside cannot feed itself.
    /// </summary>
    private void UpdateTitleBarInsets()
    {
        var scale = AppTitleBar.XamlRoot?.RasterizationScale ?? 0;
        if (scale <= 0) return;

        var bar = AppWindow.TitleBar;
        var insets = TitleBarMetrics.ComputeInsets(bar.LeftInset, bar.RightInset, bar.Height, scale);

        SetColumnWidth(TitleBarLeftInsetColumn, insets.Left);
        SetColumnWidth(TitleBarRightInsetColumn, insets.Right);
        if (AppTitleBar.Height != insets.Height) AppTitleBar.Height = insets.Height;
    }

    private static void SetColumnWidth(ColumnDefinition column, double dips)
    {
        if (column.Width.Value != dips) column.Width = new GridLength(dips);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsModal) return;
        GoBack();
    }

    private void PaneToggleButton_Click(object sender, RoutedEventArgs e) => ToggleRail();
}
