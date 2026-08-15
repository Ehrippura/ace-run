using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace ace_run.Services;

/// <summary>
/// The colour picker for a workspace or a tag: a two-column palette anchored to the swatch
/// button that asked for it.
/// </summary>
/// <remarks>
/// <para>
/// This replaces a per-row <c>ComboBox</c> that was initialized from its <c>Loaded</c> event.
/// <c>Loaded</c> fires once per container, and an <c>ItemsStackPanel</c> recycling a container
/// onto a different item only swaps the <c>DataContext</c> — so past about nine rows the combo
/// kept the previous item's selection and quietly showed the wrong colour. Building the picker
/// on demand removes the state that could go stale: nothing here outlives the flyout.
/// </para>
/// <para>
/// It is built here rather than declared in the row's <c>DataTemplate</c> for the neighbouring
/// reason — a flyout inside a template is instantiated per realized container and is *not*
/// rebuilt on recycle, so anything its handlers captured would go stale even though
/// <c>{x:Bind}</c> would not.
/// </para>
/// <para>
/// Two columns rather than one, because a single column of labelled rows is the menu this is
/// meant to replace. Each cell carries the localized colour name as visible text, which is what
/// keeps the palette usable under High Contrast: that dictionary collapses all six
/// <c>AceTagBrush*</c> to <c>SystemColorWindowTextColor</c> on purpose, so the dots alone would
/// be six identical circles. The check mark on the current entry is load-bearing for the same
/// reason — a coloured ring would be invisible there.
/// </para>
/// </remarks>
internal static class ColorSwatchFlyout
{
    private const double SwatchSize = 16;

    /// <param name="allowNone">
    /// Workspaces only. <c>WorkspaceInfo.ColorTag</c> is nullable and null is the documented
    /// "no colour, no window edge" state; a tag has no such state — <c>TagViewModel</c> coerces
    /// an empty key to <see cref="ColorKeys.Default"/> — so offering it there would be a lie.
    /// </param>
    /// <param name="onPick">Handed the chosen key, or null when the user picked "no colour".</param>
    public static void Show(FrameworkElement anchor, string? current, bool allowNone, Action<string?> onPick)
    {
        var theme = anchor.ActualTheme;

        var keys = new List<string?>();
        if (allowNone) keys.Add(null);
        foreach (var key in ColorKeys.All) keys.Add(key);

        var grid = new Grid { ColumnSpacing = 4, RowSpacing = 4 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var rows = (keys.Count + 1) / 2;
        for (var i = 0; i < rows; i++) grid.RowDefinitions.Add(new RowDefinition());

        Flyout? flyout = null;

        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            var entry = BuildEntry(key, current, theme);
            entry.Click += (_, _) =>
            {
                flyout?.Hide();
                onPick(key);
            };

            Grid.SetRow(entry, i / 2);
            Grid.SetColumn(entry, i % 2);
            grid.Children.Add(entry);
        }

        flyout = new Flyout { Content = grid };
        ThemeService.ApplyTo(flyout);
        flyout.ShowAt(anchor);
    }

    private static Button BuildEntry(string? key, string? current, ElementTheme theme)
    {
        var name = Loc.GetString(key is null ? "Color_None" : $"Color_{key}");
        var isCurrent = key == current;

        var content = new Grid { ColumnSpacing = 8 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        content.Children.Add(BuildSwatch(key, theme));

        var label = new TextBlock
        {
            Text = name,
            VerticalAlignment = VerticalAlignment.Center,
            Style = Application.Current.Resources["AceBodyStyle"] as Style
        };
        Grid.SetColumn(label, 1);
        content.Children.Add(label);

        if (isCurrent)
        {
            var check = new FontIcon
            {
                Glyph = "",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(check, 2);
            content.Children.Add(check);
        }

        var button = new Button
        {
            Content = content,
            Padding = new Thickness(8, 6, 8, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            MinWidth = 132
        };
        AutomationProperties.SetName(button, name);
        return button;
    }

    /// <summary>
    /// The dot. "No colour" is an empty ring rather than a slashed one: the same shape the
    /// row's swatch button shows for a colourless workspace, so the two read as the same thing.
    /// </summary>
    private static Shape BuildSwatch(string? key, ElementTheme theme)
    {
        var dot = new Ellipse
        {
            Width = SwatchSize,
            Height = SwatchSize,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = ColorTags.ResolveBrush(key, theme),
            Stroke = Application.Current.Resources["ControlStrongStrokeColorDefaultBrush"] as Brush,
            StrokeThickness = 1
        };
        return dot;
    }
}
