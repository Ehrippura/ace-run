using System;
using System.Collections.Generic;
using ace_run.Models;

namespace ace_run.Services;

/// <summary>Physical pixels. Deliberately not <c>Windows.Graphics.SizeInt32</c> — see <see cref="WindowPlacement"/>.</summary>
public readonly record struct PixelSize(int Width, int Height);

/// <inheritdoc cref="PixelSize"/>
public readonly record struct PixelPoint(int X, int Y);

/// <inheritdoc cref="PixelSize"/>
public readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <summary>Title bar column widths and row height, in DIPs.</summary>
public readonly record struct TitleBarInsets(double Left, double Right, double Height);

/// <summary>
/// Sizing and positioning arithmetic for windows.
/// </summary>
/// <remarks>
/// The values are physical pixels, which is what <c>AppWindow</c> deals in — every XAML
/// dimension is in DIPs, and mixing the two is the bug this whole file exists to make
/// testable. Plain records rather than the <c>Windows.Graphics</c> structs so the logic layer
/// stays free of the projection; the callers convert at the boundary.
/// </remarks>
public static class WindowPlacement
{
    // Default window size, in DIPs. A nav-pane + content silhouette; wide enough for the rail
    // plus a comfortable multi-column tile grid.
    public const int DefaultWidthDip = 1120;
    public const int DefaultHeightDip = 760;
    public const int MinWidthDip = 720;
    public const int MinHeightDip = 480;

    public static PixelSize MinimumSize(double scale)
        => new((int)(MinWidthDip * scale), (int)(MinHeightDip * scale));

    /// <summary>
    /// The size to open at: what was saved if it is usable, otherwise the default, clamped so
    /// it fits the monitor.
    /// </summary>
    /// <remarks>
    /// The clamp is not defensive tidiness. A size saved on a 4K display would otherwise
    /// restore larger than a 1080p screen and put the controls out of reach, with no way back
    /// short of editing the config by hand.
    /// </remarks>
    public static PixelSize ResolveStartupSize(WindowState? saved, double scale, PixelSize workArea)
    {
        var width = saved is { Width: > 0 } ? saved.Width : (int)(DefaultWidthDip * scale);
        var height = saved is { Height: > 0 } ? saved.Height : (int)(DefaultHeightDip * scale);

        return new PixelSize(Math.Min(width, workArea.Width), Math.Min(height, workArea.Height));
    }

    /// <summary>
    /// Centres <paramref name="size"/> over <paramref name="anchor"/>, then clamps it into
    /// <paramref name="workArea"/>.
    /// </summary>
    /// <remarks>
    /// The inner <c>Math.Max</c> in each clamp guards the case where the window is larger than
    /// the work area: the upper bound would otherwise fall below the lower one and
    /// <see cref="Math.Clamp(int,int,int)"/> would throw. In that case the window is pinned to
    /// the top-left of the work area, which is the only placement that keeps its title bar
    /// reachable.
    /// </remarks>
    public static PixelPoint CenterIn(PixelRect anchor, PixelSize size, PixelRect workArea)
    {
        var x = anchor.X + (anchor.Width - size.Width) / 2;
        var y = anchor.Y + (anchor.Height - size.Height) / 2;

        x = Math.Clamp(x, workArea.X, Math.Max(workArea.X, workArea.X + workArea.Width - size.Width));
        y = Math.Clamp(y, workArea.Y, Math.Max(workArea.Y, workArea.Y + workArea.Height - size.Height));

        return new PixelPoint(x, y);
    }
}

/// <summary>
/// The caption-strip reserve, converted from the physical pixels the OS reports into the DIPs
/// a XAML column is measured in.
/// </summary>
/// <remarks>
/// This division is the entire reason the app draws its own title bar row instead of using
/// WASDK's <c>TitleBar</c> control, which assigns the physical-pixel inset straight into a DIP
/// column and so reserves 216 DIP for a 144 DIP strip at 150% scaling — around 120 DIP of dead
/// title bar that no public property can reach.
/// </remarks>
public static class TitleBarMetrics
{
    /// <summary>The SDK's tall title bar, in DIPs — what PreferredHeightOption.Tall asks for.</summary>
    public const double TallTitleBarHeightDip = 48;

    /// <param name="heightPx">
    /// The caption height in physical pixels. Zero when the OS has not reported one yet, which
    /// falls back to <see cref="TallTitleBarHeightDip"/> — a zero height would collapse the row.
    /// </param>
    /// <remarks>
    /// Both insets are computed, not just the right one: right-to-left layouts and left-handed
    /// caption buttons move the strip to the left, and a right-only reserve would then push the
    /// workspace picker under the close button.
    /// </remarks>
    public static TitleBarInsets ComputeInsets(int leftPx, int rightPx, int heightPx, double scale)
        => new(leftPx / scale,
               rightPx / scale,
               heightPx > 0 ? heightPx / scale : TallTitleBarHeightDip);
}

/// <summary>A realized tile's bounds relative to the grid, in DIPs.</summary>
public readonly record struct TileBounds(double X, double Y, double Width, double Height);

/// <summary>
/// Where a dropped item lands in the grid.
/// </summary>
public static class DropGeometry
{
    /// <summary>
    /// The insertion index for a pointer at (<paramref name="x"/>, <paramref name="y"/>).
    /// </summary>
    /// <param name="count">Items in the collection, realized or not.</param>
    /// <param name="boundsAt">
    /// Bounds of the tile at an index, or null when it is scrolled out and has no container.
    /// Skipping those is safe: the pointer can only be over a realized one. A delegate rather
    /// than a list because this runs on every DragOver.
    /// </param>
    /// <remarks>
    /// Reading order, so the row is decided before the column. The gap between rows belongs to
    /// the row below — it is past the bottom of one and above the top of the next, which is
    /// exactly where the two branches hand over. Falling out of the loop means the pointer is
    /// past the last tile, so the item appends.
    /// </remarks>
    public static int ResolveInsertIndex(double x, double y, int count, Func<int, TileBounds?> boundsAt)
    {
        for (var index = 0; index < count; index++)
        {
            if (boundsAt(index) is not { } tile) continue;

            if (y > tile.Y + tile.Height) continue;
            if (y < tile.Y) return index;

            if (x > tile.X + tile.Width / 2) continue;
            return index;
        }

        return count;
    }
}
