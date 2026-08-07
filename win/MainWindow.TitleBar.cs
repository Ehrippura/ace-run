using ace_run.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

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
        };
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
        SetColumnWidth(TitleBarLeftInsetColumn, bar.LeftInset / scale);
        SetColumnWidth(TitleBarRightInsetColumn, bar.RightInset / scale);

        // 0 would collapse the whole row; the SDK's tall title bar is 48 DIP, which is
        // what PreferredHeightOption.Tall asks for in MainWindow's constructor.
        var height = bar.Height > 0 ? bar.Height / scale : 48;
        if (AppTitleBar.Height != height) AppTitleBar.Height = height;
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
