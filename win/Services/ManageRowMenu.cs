using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace ace_run.Services;

/// <summary>
/// The per-row overflow menu shared by the two manage dialogs.
/// </summary>
/// <remarks>
/// <para>
/// It replaces the icon buttons that used to sit on every row. Three permanently visible
/// controls per row is noise in a list of six, and it left the row with four tab stops; more
/// importantly it had no room for Move Up / Move Down, which is the only keyboard route to
/// reordering — a row holds a <c>TextBox</c>, which takes focus ahead of its container, so the
/// <c>ListView</c>'s own Ctrl+Shift+Arrow reorder gesture is never reached.
/// </para>
/// <para>
/// Built per click rather than declared in the row's <c>DataTemplate</c>: a flyout in a
/// template is instantiated once per realized container and is not rebuilt when that container
/// is recycled onto another item, so anything its handlers captured would go stale. Same
/// discipline as <c>MainWindow.BuildAppMenu</c>.
/// </para>
/// </remarks>
internal static class ManageRowMenu
{
    /// <param name="onExport">Workspaces only; omit for tags.</param>
    public static void Show(
        Button anchor,
        Action? onExport,
        Action onMoveUp,
        Action onMoveDown,
        bool canMoveUp,
        bool canMoveDown,
        Action onDelete)
    {
        var menu = new MenuFlyout();

        if (onExport is not null)
        {
            var export = new MenuFlyoutItem
            {
                Text = Loc.GetString("Workspace_Export"),
                Icon = new FontIcon { Glyph = "" }
            };
            export.Click += (_, _) => onExport();
            menu.Items.Add(export);
            menu.Items.Add(new MenuFlyoutSeparator());
        }

        var up = new MenuFlyoutItem
        {
            Text = Loc.GetString("Row_MoveUp"),
            Icon = new FontIcon { Glyph = "" },
            IsEnabled = canMoveUp
        };
        up.Click += (_, _) => onMoveUp();
        menu.Items.Add(up);

        var down = new MenuFlyoutItem
        {
            Text = Loc.GetString("Row_MoveDown"),
            Icon = new FontIcon { Glyph = "" },
            IsEnabled = canMoveDown
        };
        down.Click += (_, _) => onMoveDown();
        menu.Items.Add(down);

        menu.Items.Add(new MenuFlyoutSeparator());

        var delete = new MenuFlyoutItem
        {
            Text = Loc.GetString("DeleteButton"),
            Icon = new FontIcon { Glyph = "" }
        };
        delete.Click += (_, _) => onDelete();
        menu.Items.Add(delete);

        ThemeService.ApplyTo(menu);
        menu.ShowAt(anchor);
    }

    /// <summary>
    /// Opens a confirmation against the row's overflow button, one dispatcher turn later.
    /// </summary>
    /// <remarks>
    /// The delay is required, not defensive. A <c>MenuFlyoutItem.Click</c> fires while the menu
    /// that hosted it is still dismissing, and its light-dismiss layer swallows a second popup
    /// opened inside that window — Delete would simply do nothing, intermittently. Same shape
    /// of fix as <c>MainWindow.ScheduleIconRelease</c>: let the framework's own pass finish.
    ///
    /// The anchor is the overflow <see cref="Button"/> and never the <c>MenuFlyoutItem</c>,
    /// whose visual parent is the presenter inside the popup being torn down.
    ///
    /// Placement is right-edge aligned because the button sits at the end of a row that can be
    /// near the bottom of a 360-DIP list, where the default below-placement would be clipped.
    /// </remarks>
    public static void ConfirmDelete(Button anchor, string title, string message, Action onConfirm) =>
        anchor.DispatcherQueue.TryEnqueue(() => ConfirmFlyout.Show(
            anchor,
            title,
            message,
            Loc.GetString("DeleteButton"),
            onConfirm,
            new FlyoutShowOptions { Placement = FlyoutPlacementMode.LeftEdgeAlignedTop }));
}
