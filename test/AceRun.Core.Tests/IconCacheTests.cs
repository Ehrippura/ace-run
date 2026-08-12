using System;
using System.IO;
using System.Linq;
using ace_run.Services;
using Xunit;

namespace ace_run.Tests;

public sealed class IconCacheTests : IDisposable
{
    private readonly string _dir;

    public IconCacheTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "AceRunTests", Guid.NewGuid().ToString("N"), "icons");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    // --- PathFor ---

    [Fact]
    public void A_cache_entry_carries_no_extension()
    {
        // What gets written is an uncompressed BMP whatever the name says, and the old .png
        // suffix named a format the bytes never had. Dropping it is also what makes ClearAll's
        // sweep unfilterable — see below.
        var path = IconCache.PathFor(_dir, Guid.NewGuid());

        Assert.Equal(string.Empty, Path.GetExtension(path));
    }

    [Fact]
    public void The_entry_is_named_after_the_item_id()
    {
        var id = Guid.NewGuid();

        Assert.Equal(id.ToString("N"), Path.GetFileName(IconCache.PathFor(_dir, id)));
    }

    [Fact]
    public void The_same_id_always_maps_to_the_same_path()
    {
        var id = Guid.NewGuid();

        Assert.Equal(IconCache.PathFor(_dir, id), IconCache.PathFor(_dir, id));
    }

    // --- Invalidate ---

    [Fact]
    public void Invalidate_removes_the_entry()
    {
        var id = Guid.NewGuid();
        WriteEntry(IconCache.PathFor(_dir, id));

        IconCache.Invalidate(_dir, id);

        Assert.False(File.Exists(IconCache.PathFor(_dir, id)));
    }

    [Fact]
    public void Invalidate_leaves_other_entries_alone()
    {
        var kept = Guid.NewGuid();
        var dropped = Guid.NewGuid();
        WriteEntry(IconCache.PathFor(_dir, kept));
        WriteEntry(IconCache.PathFor(_dir, dropped));

        IconCache.Invalidate(_dir, dropped);

        Assert.True(File.Exists(IconCache.PathFor(_dir, kept)));
    }

    [Fact]
    public void Invalidate_on_an_absent_entry_is_not_an_error()
    {
        Directory.CreateDirectory(_dir);

        IconCache.Invalidate(_dir, Guid.NewGuid());
    }

    [Fact]
    public void Invalidate_on_an_absent_directory_is_not_an_error()
        => IconCache.Invalidate(_dir, Guid.NewGuid());

    // --- ClearAll ---

    [Fact]
    public void ClearAll_takes_every_file_and_reports_the_count()
    {
        WriteEntry(IconCache.PathFor(_dir, Guid.NewGuid()));
        WriteEntry(IconCache.PathFor(_dir, Guid.NewGuid()));

        Assert.Equal(2, IconCache.ClearAll(_dir));
        Assert.Empty(Directory.EnumerateFiles(_dir));
    }

    [Fact]
    public void ClearAll_collects_the_debris_a_filter_would_step_over()
    {
        // The whole reason the sweep is unfiltered. A .tmp is left by an extraction that died
        // mid-write; a <guid>.png is a pre-rename entry that no lookup can reach any more.
        // This button is the only migration either one ever gets.
        WriteEntry(IconCache.PathFor(_dir, Guid.NewGuid()));
        WriteEntry(Path.Combine(_dir, $"{Guid.NewGuid():N}.tmp"));
        WriteEntry(Path.Combine(_dir, $"{Guid.NewGuid():N}.png"));

        Assert.Equal(3, IconCache.ClearAll(_dir));
        Assert.Empty(Directory.EnumerateFiles(_dir));
    }

    [Fact]
    public void ClearAll_on_an_absent_directory_reports_zero()
        => Assert.Equal(0, IconCache.ClearAll(_dir));

    [Fact]
    public void ClearAll_on_an_empty_directory_reports_zero()
    {
        Directory.CreateDirectory(_dir);

        Assert.Equal(0, IconCache.ClearAll(_dir));
    }

    [Fact]
    public void ClearAll_does_not_descend_into_subdirectories()
    {
        // EnumerateFiles is top-level only. Nothing creates a subdirectory here today, but a
        // recursive delete would be a much bigger promise than "empty the cache".
        var nested = Path.Combine(_dir, "nested");
        Directory.CreateDirectory(nested);
        WriteEntry(Path.Combine(nested, "keep"));

        Assert.Equal(0, IconCache.ClearAll(_dir));
        Assert.True(Directory.Exists(nested));
    }

    // --- ChooseSource ---

    [Fact]
    public void A_custom_icon_that_exists_wins()
        => Assert.Equal("icon.ico",
            IconCache.ChooseSource("app.exe", "icon.ico", _ => true));

    [Fact]
    public void A_custom_icon_that_is_gone_falls_back_to_the_item()
    {
        // Not to nothing: the item's own icon is still the better answer, and a bare glyph
        // would look like the item itself had broken.
        Assert.Equal("app.exe",
            IconCache.ChooseSource("app.exe", "missing.ico", p => p == "app.exe"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void No_custom_icon_means_the_item_path(string? custom)
        => Assert.Equal("app.exe", IconCache.ChooseSource("app.exe", custom, _ => true));

    [Fact]
    public void Neither_existing_yields_null()
        => Assert.Null(IconCache.ChooseSource("app.exe", "icon.ico", _ => false));

    [Fact]
    public void A_url_item_has_no_icon_source()
    {
        // FilePath is a URL for Url items, so no file test can succeed — the templates fall
        // back to a glyph.
        Assert.Null(IconCache.ChooseSource("https://example.com", null, File.Exists));
    }

    private void WriteEntry(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1, 2, 3]);
    }
}

public class IconExtractionPolicyTests
{
    [Fact]
    public void E_PENDING_is_worth_repeating()
    {
        // The bug this pins. Treating it as permanent is what left tiles blank for good:
        // nothing re-runs an extraction, so an item that lost the race stayed on the fallback
        // glyph until the cache was reset by hand.
        Assert.True(IconExtractionPolicy.IsRetryable(IconExtractionPolicy.EPending));
    }

    [Theory]
    [InlineData(0)]                          // S_OK
    [InlineData(unchecked((int)0x80070002))] // file not found
    [InlineData(unchecked((int)0x80004005))] // E_FAIL
    public void Everything_else_is_permanent(int hresult)
        => Assert.False(IconExtractionPolicy.IsRetryable(hresult));

    [Fact]
    public void The_backoff_grows()
    {
        var delays = IconExtractionPolicy.BackoffMs;

        Assert.NotEmpty(delays);
        for (var i = 1; i < delays.Count; i++)
            Assert.True(delays[i] > delays[i - 1], $"delay {i} did not grow");
    }

    [Fact]
    public void The_first_retry_is_short()
    {
        // Extraction is single-digit milliseconds once the shell is warm, so the first retry
        // should almost always land. A long first delay would show as a visible blank tile.
        Assert.True(IconExtractionPolicy.BackoffMs[0] <= 50);
    }

    [Fact]
    public void There_is_one_more_attempt_than_there_are_delays()
        => Assert.Equal(IconExtractionPolicy.BackoffMs.Count + 1, IconExtractionPolicy.MaxAttempts);

    [Fact]
    public void Every_attempt_within_budget_gets_a_delay()
    {
        for (var attempt = 0; attempt < IconExtractionPolicy.BackoffMs.Count; attempt++)
            Assert.Equal(IconExtractionPolicy.BackoffMs[attempt],
                         IconExtractionPolicy.DelayForAttempt(attempt));
    }

    [Fact]
    public void The_attempt_past_the_budget_gives_up()
        => Assert.Null(IconExtractionPolicy.DelayForAttempt(IconExtractionPolicy.BackoffMs.Count));

    [Fact]
    public void A_negative_attempt_gives_up_rather_than_throwing()
        => Assert.Null(IconExtractionPolicy.DelayForAttempt(-1));

    [Fact]
    public void The_whole_retry_budget_stays_under_a_second()
    {
        // This runs while a tile shows the fallback glyph, so the ceiling is a UI budget, not
        // an arbitrary one.
        Assert.True(IconExtractionPolicy.BackoffMs.Sum() < 1000);
    }
}
