using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace ace_run.Services;

internal static class IconService
{
    private static readonly string CacheDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "AceRun", "icons");

    /// <summary>
    /// Extractions currently running, keyed by cache path.
    ///
    /// The same item is routinely asked for twice at once — an add loads the icon eagerly
    /// while the container realizing for that same item loads it again, and a search row and
    /// a grid tile are the same view model. On a cold cache both calls used to reach
    /// <see cref="ExtractAndCacheIconAsync"/>, and the loser of the write hit a sharing
    /// violation, swallowed it, and returned null — so a freshly dropped app came up with no
    /// icon roughly half the time. Handing every caller the one running extraction removes
    /// the race and the duplicated work with it.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Task> Extractions =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Cache path for an item. Deliberately **without an extension**: what gets written is
    /// whatever bytes <c>GetThumbnailAsync</c> handed back, which in practice is an
    /// uncompressed 32bpp BMP, not a PNG — the old <c>.png</c> suffix named a format the file
    /// never had. Nothing reads it by extension either; <see cref="BitmapImage.SetSourceAsync"/>
    /// sniffs the format through WIC, which is the only reason the mislabelled files worked.
    /// </summary>
    private static string CachePathFor(Guid itemId) => Path.Combine(CacheDir, $"{itemId:N}");

    /// <summary>
    /// <c>E_PENDING</c> — the shell's "the thumbnail is not ready, ask again" signal.
    /// </summary>
    private const int EPending = unchecked((int)0x8000000A);

    /// <summary>
    /// Serializes thumbnail extraction to one at a time.
    /// <para>
    /// The shell refuses overlapping extractions rather than queueing them: with a folder's
    /// worth of tiles realizing at once on a cold cache, the first request wins and every
    /// other one comes straight back with <see cref="EPending"/> — measured at four tiles,
    /// three failed before the winner's thumbnail had even arrived. Letting one through at a
    /// time turns that contention into a short queue. It costs nothing noticeable: extraction
    /// runs single-digit milliseconds once the shell is warm, and the whole thing is off the
    /// UI thread's critical path anyway.
    /// </para>
    /// </summary>
    private static readonly SemaphoreSlim ExtractionGate = new(1, 1);

    public static async Task<BitmapImage?> GetIconAsync(string filePath, Guid itemId, string? customIconPath = null)
    {
        var iconSource = !string.IsNullOrEmpty(customIconPath) && File.Exists(customIconPath)
            ? customIconPath
            : filePath;

        if (!File.Exists(iconSource))
            return null;

        var cachePath = CachePathFor(itemId);

        if (!File.Exists(cachePath))
            await EnsureCachedAsync(iconSource, cachePath);

        if (!File.Exists(cachePath))
            return null;

        try
        {
            var bytes = await File.ReadAllBytesAsync(cachePath);
            using var ms = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(ms.AsRandomAccessStream());
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Drops the cached icon for an item — on a path or custom-icon change, and when the item
    /// itself is deleted. The pending extraction goes with it: the file it is about to write
    /// was extracted from the source this call has just declared stale, so a later caller must
    /// start over rather than be handed that task.
    /// </summary>
    public static void InvalidateCache(Guid itemId)
    {
        var cachePath = CachePathFor(itemId);
        Extractions.TryRemove(cachePath, out _);
        try { if (File.Exists(cachePath)) File.Delete(cachePath); }
        catch { }
    }

    /// <inheritdoc cref="InvalidateCache(Guid)"/>
    public static void InvalidateCache(IEnumerable<Guid> itemIds)
    {
        foreach (var id in itemIds)
            InvalidateCache(id);
    }

    /// <summary>
    /// Empties the whole icon cache and reports how many files went. Nothing is lost that
    /// cannot be extracted again — this is the way out when an item's exe has been updated
    /// with a new icon, or when a cache entry has gone bad.
    /// <para>
    /// The sweep is unfiltered. Cache entries carry no extension (see
    /// <see cref="CachePathFor"/>) so there is no pattern left to match on, and the directory
    /// is ours alone — nothing but this class ever writes into it. Taking everything also
    /// collects the two kinds of debris a filter would have stepped over: a <c>.tmp</c> left
    /// by an extraction that died mid-write, and the <c>&lt;guid&gt;.png</c> entries written
    /// before the extension was dropped, which no lookup can reach any more. Those are why
    /// this button is the migration — nothing renames them, the next paint just re-extracts.
    /// </para>
    /// <para>
    /// A file that refuses to delete is skipped rather than aborting the sweep.
    /// </para>
    /// </summary>
    public static int ClearCache()
    {
        Extractions.Clear();

        if (!Directory.Exists(CacheDir))
            return 0;

        var cleared = 0;

        foreach (var file in Directory.EnumerateFiles(CacheDir))
        {
            try
            {
                File.Delete(file);
                cleared++;
            }
            catch { }
        }

        return cleared;
    }

    private static async Task EnsureCachedAsync(string iconSource, string cachePath)
    {
        // GetOrAdd's factory can run more than once under contention, but every caller is
        // handed the same task, which is the whole point.
        var extraction = Extractions.GetOrAdd(cachePath, _ => ExtractAndCacheIconAsync(iconSource, cachePath));

        try
        {
            await extraction;
        }
        finally
        {
            // Remove only if this is still the task we ran: an InvalidateCache in between may
            // already have cleared the slot and a fresh extraction taken it.
            Extractions.TryRemove(new KeyValuePair<string, Task>(cachePath, extraction));
        }
    }

    /// <summary>
    /// Extracts one icon into the cache, retrying while the shell says it is not ready yet.
    /// <para>
    /// <see cref="EPending"/> is a request to come back, not a failure, and treating it as one
    /// is what left tiles permanently iconless: nothing re-runs an extraction, because
    /// <c>LoadIconAsync</c> only fires when a container realizes, so an item that lost the
    /// race stayed blank until the cache was reset by hand. The gate makes this rare and the
    /// retries cover what is left — a genuinely cold shell can answer <c>E_PENDING</c> even
    /// with nothing else in flight.
    /// </para>
    /// </summary>
    private static async Task ExtractAndCacheIconAsync(string filePath, string cachePath)
    {
        // Delays between attempts. Extraction itself is single-digit milliseconds, so the
        // first retry almost always lands; the longer tails exist for a cold shell cache.
        int[] backoffMs = [30, 80, 200, 500];

        for (var attempt = 0; ; attempt++)
        {
            if (await TryExtractAsync(filePath, cachePath))
                return;

            if (attempt >= backoffMs.Length)
                return;

            await Task.Delay(backoffMs[attempt]);
        }
    }

    /// <returns>
    /// True when the icon is cached or the failure is permanent; false only when the shell
    /// answered <see cref="EPending"/> and the call is worth repeating.
    /// </returns>
    private static async Task<bool> TryExtractAsync(string filePath, string cachePath)
    {
        var tempPath = cachePath + ".tmp";

        await ExtractionGate.WaitAsync();

        try
        {
            Directory.CreateDirectory(CacheDir);

            var storageFile = await StorageFile.GetFileFromPathAsync(filePath);
            using var thumbnail = await storageFile.GetThumbnailAsync(
                ThumbnailMode.SingleItem, 48, ThumbnailOptions.UseCurrentScale);

            if (thumbnail == null || thumbnail.Size == 0)
                return true;

            var size = (uint)thumbnail.Size;
            var dataReader = new DataReader(thumbnail.GetInputStreamAt(0));
            await dataReader.LoadAsync(size);
            var bytes = new byte[size];
            dataReader.ReadBytes(bytes);
            dataReader.DetachStream();

            // Written beside the target and renamed into place, because File.Exists(cachePath)
            // is the "is it cached?" test above: writing in place creates the file first and
            // fills it after, leaving a window where that test passes and the read that follows
            // gets a truncated file. A rename has no such window.
            await File.WriteAllBytesAsync(tempPath, bytes);
            File.Move(tempPath, cachePath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            // Silently fail — icon just won't be shown
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch { }

            return ex.HResult != EPending;
        }
        finally
        {
            ExtractionGate.Release();
        }
    }
}
