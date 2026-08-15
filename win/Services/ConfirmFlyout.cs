using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace ace_run.Services;

/// <summary>
/// A yes/no confirmation anchored to the button that asked for it, wearing a
/// <see cref="ContentDialog"/>'s shape: title, body, separator, two equal buttons.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="Flyout"/> and not a <see cref="ContentDialog"/> because both callers are
/// themselves inside a dialog, and WinUI allows one at a time — a nested one silently fails to
/// open, which would make Delete look broken. So the dialog's *appearance* is reproduced here
/// rather than borrowed: same surface and command-area brushes, same separator, same 24-inset
/// content over a footer of two stretched buttons.
/// </para>
/// <para>
/// Neither button is accented. That matches WinUI's own <c>DefaultButton="None"</c> rendering,
/// and a destructive action should not be the one the eye lands on first.
/// </para>
/// </remarks>
internal static class ConfirmFlyout
{
    private const double DialogWidth = 320;
    private const double Inset = 24;

    /// <param name="title">The question. Body text carries the consequence.</param>
    /// <param name="options">
    /// Placement control for callers that need it. A row near the bottom of a long list wants
    /// the confirmation beside it rather than below, where it would be clipped by the dialog.
    /// </param>
    public static void Show(
        FrameworkElement target,
        string title,
        string message,
        string confirmText,
        Action onConfirm,
        FlyoutShowOptions? options = null)
    {
        // Declared before the handlers so they can close the flyout they belong to.
        Flyout? flyout = null;

        var confirm = new Button
        {
            Content = confirmText,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        confirm.Click += (_, _) =>
        {
            flyout?.Hide();
            onConfirm();
        };

        var cancel = new Button
        {
            Content = Loc.GetString("CancelButton"),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        cancel.Click += (_, _) => flyout?.Hide();

        flyout = new Flyout
        {
            Content = BuildBody(title, message, confirm, cancel),
            FlyoutPresenterStyle = PresenterStyle()
        };
        ThemeService.ApplyTo(flyout);

        if (options is null) flyout.ShowAt(target);
        else flyout.ShowAt(target, options);
    }

    private static Grid BuildBody(string title, string message, Button confirm, Button cancel)
    {
        // No explicit Width: the presenter's Min/MaxWidth sizes this, and a Grid pinned to
        // DialogWidth overflowed the presenter's content area by the 1px border on each side,
        // which showed up as a horizontal scrollbar sitting under the buttons.
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var text = new StackPanel { Spacing = 8, Padding = new Thickness(Inset) };
        text.Children.Add(new TextBlock
        {
            Text = title,
            Style = Style("AceDisplayStyle"),
            // The style trims; a title that has to wrap should wrap.
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.None
        });
        text.Children.Add(new TextBlock
        {
            Text = message,
            Style = Style("AceBodyStyle"),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.None
        });
        root.Children.Add(text);

        var footer = new Grid
        {
            ColumnSpacing = 8,
            Padding = new Thickness(Inset),
            Background = Brush("ContentDialogTopOverlay", "LayerFillColorDefaultBrush"),
            BorderBrush = Brush("ContentDialogSeparatorBorderBrush", "DividerStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            // Follows the surface, so the command area does not square off the bottom corners.
            CornerRadius = new CornerRadius(0, 0, 7, 7)
        };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(cancel, 1);
        footer.Children.Add(confirm);
        footer.Children.Add(cancel);
        Grid.SetRow(footer, 1);
        root.Children.Add(footer);

        return root;
    }

    /// <summary>
    /// Strips the presenter back to a bare surface: the padding and the separator are the
    /// body's own, because the footer has to reach the flyout's edges to read as a command bar.
    /// </summary>
    private static Style PresenterStyle()
    {
        var style = new Style(typeof(FlyoutPresenter));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.BackgroundProperty,
            Brush("ContentDialogBackground", "SolidBackgroundFillColorBaseBrush")));
        style.Setters.Add(new Setter(Control.BorderBrushProperty,
            Brush("ContentDialogBorderBrush", "SurfaceStrokeColorDefaultBrush")));
        style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(8)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, DialogWidth));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, DialogWidth));
        // Belt and braces against the scrollbar: nothing here should ever scroll sideways.
        style.Setters.Add(new Setter(ScrollViewer.HorizontalScrollBarVisibilityProperty,
            ScrollBarVisibility.Disabled));
        style.Setters.Add(new Setter(ScrollViewer.HorizontalScrollModeProperty, ScrollMode.Disabled));
        return style;
    }

    // ContentDialog's own resource keys are not contractual across WinUI versions, so every
    // lookup names a Fluent brush that certainly exists as its fallback.
    private static Brush? Brush(string key, string fallbackKey) =>
        (Lookup(key) ?? Lookup(fallbackKey)) as Brush;

    private static Style? Style(string key) => Lookup(key) as Style;

    private static object? Lookup(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true ? value : null;
}
