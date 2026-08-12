using System;
using System.Collections.Generic;
using System.IO;

namespace ace_run.Services;

/// <summary>
/// The icon cache as files on disk, with no opinion about how an icon is produced —
/// extraction needs WinRT and lives in the app.
/// </summary>
public static class IconCache
{
    /// <summary>
    /// Cache path for an item. Deliberately <b>without an extension</b>: what gets written is
    /// whatever bytes the shell handed back, which in practice is an uncompressed 32bpp BMP,
    /// not a PNG — the old <c>.png</c> suffix named a format the file never had. Nothing reads
    /// it by extension either; the bitmap decoder sniffs the format through WIC, which is the
    /// only reason the mislabelled files ever worked.
    /// </summary>
    public static string PathFor(string iconsDir, Guid itemId)
        => Path.Combine(iconsDir, $"{itemId:N}");

    /// <summary>
    /// Drops one item's cached icon — on a path or custom-icon change, and when the item is
    /// deleted. Absent file is not an error.
    /// </summary>
    public static void Invalidate(string iconsDir, Guid itemId)
    {
        var path = PathFor(iconsDir, itemId);

        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // Locked by a read in flight. The stale entry costs a wrong icon until the next
            // reset, which beats failing an edit the user did ask for.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Empties the whole cache and reports how many files went.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sweep is <b>unfiltered</b>, and that is the point. Cache entries carry no extension
    /// (see <see cref="PathFor"/>) so there is no pattern left to match on, and the directory
    /// is ours alone. Taking everything also collects the two kinds of debris a filter would
    /// step over: a <c>.tmp</c> left by an extraction that died mid-write, and the
    /// <c>&lt;guid&gt;.png</c> entries written before the extension was dropped, which no
    /// lookup can reach any more. Those are why this is the migration — nothing renames them,
    /// the next paint just re-extracts.
    /// </para>
    /// <para>
    /// A file that refuses to delete is skipped rather than aborting the sweep.
    /// </para>
    /// </remarks>
    public static int ClearAll(string iconsDir)
    {
        if (!Directory.Exists(iconsDir)) return 0;

        var removed = 0;

        foreach (var file in Directory.EnumerateFiles(iconsDir))
        {
            try
            {
                File.Delete(file);
                removed++;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return removed;
    }

    /// <summary>
    /// Which file an icon should be extracted from: the custom icon when one is set and
    /// present, otherwise the item's own path.
    /// </summary>
    /// <param name="exists">
    /// File-existence test, injected so this rule can be exercised without a filesystem.
    /// </param>
    /// <returns>Null when neither candidate exists — the caller falls back to a glyph.</returns>
    /// <remarks>
    /// A custom icon pointing at a file that has since been deleted falls back to the item
    /// rather than to nothing: the item's own icon is still the better answer, and silently
    /// showing a glyph would look like the item itself had broken.
    /// </remarks>
    public static string? ChooseSource(string filePath, string? customIconPath, Func<string, bool> exists)
    {
        if (!string.IsNullOrEmpty(customIconPath) && exists(customIconPath))
            return customIconPath;

        return exists(filePath) ? filePath : null;
    }
}

/// <summary>
/// When to retry a thumbnail extraction, and when to give up.
/// </summary>
public static class IconExtractionPolicy
{
    /// <summary>
    /// <c>E_PENDING</c> — the shell's "the thumbnail is not ready, ask again" signal.
    /// </summary>
    public const int EPending = unchecked((int)0x8000000A);

    /// <summary>
    /// Delays between attempts, in milliseconds. Extraction itself is single-digit
    /// milliseconds, so the first retry almost always lands; the longer tails exist for a
    /// cold shell cache.
    /// </summary>
    public static IReadOnlyList<int> BackoffMs { get; } = [30, 80, 200, 500];

    /// <summary>Total attempts before giving up — one more than there are delays.</summary>
    public static int MaxAttempts => BackoffMs.Count + 1;

    /// <summary>
    /// How long to wait before attempt <paramref name="attempt"/> + 1, or null when the
    /// budget is spent.
    /// </summary>
    public static int? DelayForAttempt(int attempt)
        => attempt >= 0 && attempt < BackoffMs.Count ? BackoffMs[attempt] : null;

    /// <summary>
    /// Is this failure worth repeating?
    /// </summary>
    /// <remarks>
    /// Only <see cref="EPending"/> is. Treating it as a permanent failure is what left tiles
    /// blank for good: nothing re-runs an extraction, because the load only fires when a
    /// container realizes, so an item that lost the race stayed on the fallback glyph until
    /// the cache was reset by hand.
    /// </remarks>
    public static bool IsRetryable(int hresult) => hresult == EPending;
}
