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

/// <summary>
/// Turns an item into a bitmap, going through the disk cache first.
/// </summary>
/// <remarks>
/// What lives here is what genuinely needs WinUI or the shell: <see cref="BitmapImage"/>,
/// <see cref="StorageFile"/> thumbnail extraction, and the two pieces of concurrency control
/// around them. The cache's own rules — where a file goes, what a sweep takes, which source an
/// icon comes from — are in <see cref="IconCache"/>, and when to retry is in
/// <see cref="IconExtractionPolicy"/>; both are testable without a XAML runtime.
/// </remarks>
internal static class IconService
{
    private static string CacheDir => AceRunPaths.Default.IconsDir;

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
    /// Serializes thumbnail extraction to one at a time.
    /// <para>
    /// The shell refuses overlapping extractions rather than queueing them: with a folder's
    /// worth of tiles realizing at once on a cold cache, the first request wins and every
    /// other one comes straight back with <see cref="IconExtractionPolicy.EPending"/> —
    /// measured at four tiles, three failed before the winner's thumbnail had even arrived.
    /// Letting one through at a time turns that contention into a short queue. It costs
    /// nothing noticeable: extraction runs single-digit milliseconds once the shell is warm,
    /// and the whole thing is off the UI thread's critical path anyway.
    /// </para>
    /// </summary>
    private static readonly SemaphoreSlim ExtractionGate = new(1, 1);

    public static async Task<BitmapImage?> GetIconAsync(string filePath, Guid itemId, string? customIconPath = null)
    {
        var iconSource = IconCache.ChooseSource(filePath, customIconPath, File.Exists);
        if (iconSource is null)
            return null;

        var cachePath = IconCache.PathFor(CacheDir, itemId);

        // The gate never touches the warm path: extraction only happens when the cache file
        // is absent.
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
        Extractions.TryRemove(IconCache.PathFor(CacheDir, itemId), out _);
        IconCache.Invalidate(CacheDir, itemId);
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
    /// with a new icon, or when a cache entry has gone bad. See
    /// <see cref="IconCache.ClearAll"/> for why the sweep is unfiltered.
    /// </summary>
    public static int ClearCache()
    {
        Extractions.Clear();
        return IconCache.ClearAll(CacheDir);
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
    /// The schedule and the give-up point are <see cref="IconExtractionPolicy"/>'s.
    /// </summary>
    private static async Task ExtractAndCacheIconAsync(string filePath, string cachePath)
    {
        for (var attempt = 0; ; attempt++)
        {
            if (await TryExtractAsync(filePath, cachePath))
                return;

            if (IconExtractionPolicy.DelayForAttempt(attempt) is not int delay)
                return;

            await Task.Delay(delay);
        }
    }

    /// <returns>
    /// True when the icon is cached or the failure is permanent; false only when the shell
    /// answered <see cref="IconExtractionPolicy.EPending"/> and the call is worth repeating.
    /// </returns>
    private static async Task<bool> TryExtractAsync(string filePath, string cachePath)
    {
        var tempPath = cachePath + ".tmp";

        await ExtractionGate.WaitAsync();

        try
        {
            Directory.CreateDirectory(CacheDir);

            var storageFile = await StorageFile.GetFileFromPathAsync(filePath);

            // ThumbnailMode.SingleItem, 48, UseCurrentScale: 48 because it is both the largest
            // place the icon is drawn (the tile; search rows are 20) and a native icon band, so
            // nothing resamples; SingleItem because ListView mode is the one scoped to <= 40;
            // UseCurrentScale because requestedSize is physical pixels.
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

            return !IconExtractionPolicy.IsRetryable(ex.HResult);
        }
        finally
        {
            ExtractionGate.Release();
        }
    }
}
