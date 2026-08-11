using System;
using ace_run.Models;
using ace_run.Services;
using Xunit;

namespace ace_run.Tests;

public class TitleBarMetricsTests
{
    [Fact]
    public void At_100_percent_the_inset_passes_through()
    {
        var insets = TitleBarMetrics.ComputeInsets(0, 144, 48, scale: 1.0);

        Assert.Equal(0, insets.Left);
        Assert.Equal(144, insets.Right);
        Assert.Equal(48, insets.Height);
    }

    [Fact]
    public void At_150_percent_a_144_dip_strip_reserves_144_dip_not_216()
    {
        // The regression this method exists for. WASDK's own TitleBar control assigns the
        // physical-pixel inset straight into a DIP column, leaving ~120 DIP of dead title bar
        // at this scale with no public property able to reach it.
        var insets = TitleBarMetrics.ComputeInsets(0, 216, 72, scale: 1.5);

        Assert.Equal(144, insets.Right);
        Assert.Equal(48, insets.Height);
    }

    [Fact]
    public void Both_insets_are_computed_not_just_the_right_one()
    {
        // RTL layouts and left-handed caption buttons move the strip to the left; a right-only
        // reserve would push the workspace picker under the close button.
        var insets = TitleBarMetrics.ComputeInsets(216, 0, 72, scale: 1.5);

        Assert.Equal(144, insets.Left);
        Assert.Equal(0, insets.Right);
    }

    [Fact]
    public void A_height_the_os_has_not_reported_falls_back_rather_than_collapsing_the_row()
    {
        var insets = TitleBarMetrics.ComputeInsets(0, 0, heightPx: 0, scale: 1.5);

        Assert.Equal(TitleBarMetrics.TallTitleBarHeightDip, insets.Height);
    }
}

public class WindowPlacementTests
{
    [Fact]
    public void With_nothing_saved_the_default_size_is_scaled()
    {
        var size = WindowPlacement.ResolveStartupSize(null, 1.5, new PixelSize(10000, 10000));

        Assert.Equal((int)(WindowPlacement.DefaultWidthDip * 1.5), size.Width);
        Assert.Equal((int)(WindowPlacement.DefaultHeightDip * 1.5), size.Height);
    }

    [Fact]
    public void A_saved_size_is_used_as_is()
    {
        // Already in physical pixels — it was written by AppWindow, not by XAML.
        var size = WindowPlacement.ResolveStartupSize(
            new WindowState { Width = 1600, Height = 900 }, 1.5, new PixelSize(10000, 10000));

        Assert.Equal(new PixelSize(1600, 900), size);
    }

    [Fact]
    public void A_size_saved_on_a_4k_display_is_clamped_to_a_1080p_screen()
    {
        // Without this the window restores larger than the screen and puts its own controls
        // out of reach, with no way back short of editing the config by hand.
        var size = WindowPlacement.ResolveStartupSize(
            new WindowState { Width = 3840, Height = 2160 }, 1.0, new PixelSize(1920, 1040));

        Assert.Equal(new PixelSize(1920, 1040), size);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 800)]
    [InlineData(1000, 0)]
    public void A_saved_size_with_a_non_positive_dimension_falls_back_to_the_default(int w, int h)
    {
        var size = WindowPlacement.ResolveStartupSize(
            new WindowState { Width = w, Height = h }, 1.0, new PixelSize(10000, 10000));

        if (w <= 0) Assert.Equal(WindowPlacement.DefaultWidthDip, size.Width);
        if (h <= 0) Assert.Equal(WindowPlacement.DefaultHeightDip, size.Height);
    }

    [Fact]
    public void The_minimum_size_scales()
    {
        var minimum = WindowPlacement.MinimumSize(2.0);

        Assert.Equal(WindowPlacement.MinWidthDip * 2, minimum.Width);
        Assert.Equal(WindowPlacement.MinHeightDip * 2, minimum.Height);
    }

    // --- CenterIn ---

    [Fact]
    public void A_window_centres_over_its_owner()
    {
        var position = WindowPlacement.CenterIn(
            anchor: new PixelRect(100, 100, 800, 600),
            size: new PixelSize(400, 300),
            workArea: new PixelRect(0, 0, 1920, 1040));

        Assert.Equal(new PixelPoint(300, 250), position);
    }

    [Fact]
    public void A_window_centred_off_the_right_edge_is_pulled_back_in()
    {
        var position = WindowPlacement.CenterIn(
            anchor: new PixelRect(1800, 0, 100, 100),
            size: new PixelSize(400, 300),
            workArea: new PixelRect(0, 0, 1920, 1040));

        Assert.Equal(1920 - 400, position.X);
    }

    [Fact]
    public void A_window_centred_off_the_left_edge_is_pushed_back_in()
    {
        var position = WindowPlacement.CenterIn(
            anchor: new PixelRect(0, 0, 100, 100),
            size: new PixelSize(400, 300),
            workArea: new PixelRect(0, 0, 1920, 1040));

        Assert.Equal(0, position.X);
    }

    [Fact]
    public void A_window_larger_than_the_work_area_pins_to_its_top_left()
    {
        // The Math.Max guard: without it the clamp's upper bound falls below its lower bound
        // and Math.Clamp throws. Pinning top-left is the only placement that keeps the title
        // bar reachable.
        var position = WindowPlacement.CenterIn(
            anchor: new PixelRect(0, 0, 800, 600),
            size: new PixelSize(2000, 1200),
            workArea: new PixelRect(0, 0, 1920, 1040));

        Assert.Equal(new PixelPoint(0, 0), position);
    }

    [Fact]
    public void A_work_area_on_a_second_monitor_keeps_its_origin()
    {
        // A monitor to the left of the primary has negative coordinates.
        var position = WindowPlacement.CenterIn(
            anchor: new PixelRect(-1920, 0, 1920, 1080),
            size: new PixelSize(400, 300),
            workArea: new PixelRect(-1920, 0, 1920, 1080));

        Assert.Equal(-1920 + (1920 - 400) / 2, position.X);
    }
}

public class DropGeometryTests
{
    // A 3-across grid of 100x100 tiles at the origin.
    private static TileBounds? Grid3x2(int index) => index switch
    {
        0 => new TileBounds(0, 0, 100, 100),
        1 => new TileBounds(100, 0, 100, 100),
        2 => new TileBounds(200, 0, 100, 100),
        3 => new TileBounds(0, 100, 100, 100),
        4 => new TileBounds(100, 100, 100, 100),
        5 => new TileBounds(200, 100, 100, 100),
        _ => null
    };

    [Theory]
    // Left half of a tile inserts before it.
    [InlineData(10, 50, 0)]
    [InlineData(110, 50, 1)]
    // Right half hands over to the next.
    [InlineData(90, 50, 1)]
    [InlineData(190, 50, 2)]
    // Second row.
    [InlineData(10, 150, 3)]
    [InlineData(190, 150, 5)]
    public void The_pointer_inserts_before_the_tile_it_is_on(double x, double y, int expected)
        => Assert.Equal(expected, DropGeometry.ResolveInsertIndex(x, y, 6, Grid3x2));

    [Fact]
    public void Past_the_last_tile_appends()
        => Assert.Equal(6, DropGeometry.ResolveInsertIndex(250, 250, 6, Grid3x2));

    [Fact]
    public void Past_the_end_of_a_row_falls_into_the_next_row()
    {
        // Right of the last tile in row 0 but still within its band: the scan continues and
        // lands on the first tile of row 1.
        Assert.Equal(3, DropGeometry.ResolveInsertIndex(290, 50, 6, Grid3x2));
    }

    [Fact]
    public void The_gap_between_rows_belongs_to_the_row_below()
    {
        // Past the bottom of row 0 and above the top of row 1 — exactly where the two
        // branches hand over. This is the case that regressed before.
        TileBounds? spaced(int i) => i switch
        {
            0 => new TileBounds(0, 0, 100, 100),
            1 => new TileBounds(0, 120, 100, 100),  // 20px gutter
            _ => null
        };

        Assert.Equal(1, DropGeometry.ResolveInsertIndex(50, 110, 2, spaced));
    }

    [Fact]
    public void Above_the_first_tile_inserts_at_the_front()
        => Assert.Equal(0, DropGeometry.ResolveInsertIndex(50, -10, 6, Grid3x2));

    [Fact]
    public void Unrealized_tiles_are_skipped()
    {
        // Scrolled-out rows have no container. Skipping them is safe: the pointer can only be
        // over a realized one.
        TileBounds? sparse(int i) => i == 2 ? new TileBounds(0, 0, 100, 100) : null;

        Assert.Equal(2, DropGeometry.ResolveInsertIndex(10, 50, 5, sparse));
    }

    [Fact]
    public void A_grid_with_nothing_realized_appends()
        => Assert.Equal(4, DropGeometry.ResolveInsertIndex(50, 50, 4, _ => null));

    [Fact]
    public void An_empty_grid_appends_at_zero()
        => Assert.Equal(0, DropGeometry.ResolveInsertIndex(50, 50, 0, _ => null));
}
