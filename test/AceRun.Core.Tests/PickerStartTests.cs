using System;
using System.IO;
using ace_run.Services;
using Xunit;

namespace ace_run.Tests;

public class PickerStartTests
{
    // --- DirectoryOf ---

    [Fact]
    public void A_file_path_yields_its_folder()
        => Assert.Equal(@"C:\Program Files\App", PickerStart.DirectoryOf(@"C:\Program Files\App\app.exe"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_box_yields_nothing(string? path)
        => Assert.Null(PickerStart.DirectoryOf(path));

    [Fact]
    public void Surrounding_whitespace_is_ignored()
        => Assert.Equal(@"C:\Apps", PickerStart.DirectoryOf("  C:\\Apps\\app.exe  "));

    [Fact]
    public void A_bare_filename_has_no_folder_to_name()
        => Assert.Null(PickerStart.DirectoryOf("app.exe"));

    [Fact]
    public void A_drive_root_has_no_parent()
        => Assert.Null(PickerStart.DirectoryOf(@"C:\"));

    // --- FirstExisting ---

    [Fact]
    public void The_first_candidate_that_exists_wins()
        => Assert.Equal(@"C:\Second",
            PickerStart.FirstExisting(p => p == @"C:\Second", @"C:\First", @"C:\Second"));

    [Fact]
    public void Order_is_preference_order()
    {
        // Both exist; the caller listed the current value first and must get it.
        Assert.Equal(@"C:\First", PickerStart.FirstExisting(_ => true, @"C:\First", @"C:\Second"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_candidates_are_skipped(string? blank)
        => Assert.Equal(@"C:\Real", PickerStart.FirstExisting(_ => true, blank, @"C:\Real"));

    [Fact]
    public void Nothing_existing_yields_null()
        => Assert.Null(PickerStart.FirstExisting(_ => false, @"C:\First", @"C:\Second"));

    [Fact]
    public void No_candidates_at_all_yields_null()
        => Assert.Null(PickerStart.FirstExisting(_ => true));

    [Theory]
    [InlineData("relative")]
    [InlineData(@"relative\path")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData(@"\rooted-but-driveless")]
    public void Relative_paths_are_skipped_rather_than_resolved(string relative)
    {
        // Directory.Exists would resolve these against the process's current directory —
        // wherever the app happened to be launched from, which has nothing to do with the item
        // being edited — and open the picker somewhere the user never named.
        Assert.Null(PickerStart.FirstExisting(_ => true, relative));
    }

    [Fact]
    public void A_url_never_reaches_the_existence_test()
    {
        // The icon picker offers the item's own path as a fallback candidate, and for a URL
        // item that path is an address.
        Assert.Null(PickerStart.FirstExisting(
            _ => throw new InvalidOperationException("should not be tested"),
            PickerStart.DirectoryOf("https://example.com/page")));
    }

    // --- The two together, against a real filesystem ---

    [Fact]
    public void The_folder_holding_a_real_file_is_found()
    {
        var dir = Path.Combine(Path.GetTempPath(), "AceRunTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var file = Path.Combine(dir, "app.exe");
            File.WriteAllBytes(file, [1]);

            Assert.Equal(dir, PickerStart.FirstExisting(Directory.Exists, PickerStart.DirectoryOf(file)));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void A_folder_that_has_been_deleted_falls_through()
    {
        var gone = Path.Combine(Path.GetTempPath(), "AceRunTests", Guid.NewGuid().ToString("N"));

        Assert.Null(PickerStart.FirstExisting(Directory.Exists, gone));
    }
}
