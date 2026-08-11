using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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

    private static string CachePathFor(Guid itemId) => Path.Combine(CacheDir, $"{itemId:N}.png");

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
    /// Only <c>*.png</c> is touched, so anything else that ends up in the folder is left
    /// alone, and a file that refuses to delete is skipped rather than aborting the sweep.
    /// </para>
    /// </summary>
    public static int ClearCache()
    {
        Extractions.Clear();

        if (!Directory.Exists(CacheDir))
            return 0;

        var cleared = 0;

        foreach (var file in Directory.EnumerateFiles(CacheDir, "*.png"))
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

    private static async Task ExtractAndCacheIconAsync(string filePath, string cachePath)
    {
        var tempPath = cachePath + ".tmp";

        try
        {
            Directory.CreateDirectory(CacheDir);

            var storageFile = await StorageFile.GetFileFromPathAsync(filePath);
            using var thumbnail = await storageFile.GetThumbnailAsync(
                ThumbnailMode.SingleItem, 48, ThumbnailOptions.UseCurrentScale);

            if (thumbnail == null || thumbnail.Size == 0)
                return;

            var size = (uint)thumbnail.Size;
            var dataReader = new DataReader(thumbnail.GetInputStreamAt(0));
            await dataReader.LoadAsync(size);
            var bytes = new byte[size];
            dataReader.ReadBytes(bytes);
            dataReader.DetachStream();

            // Written beside the target and renamed into place, because File.Exists(cachePath)
            // is the "is it cached?" test above: writing in place creates the file first and
            // fills it after, leaving a window where that test passes and the read that follows
            // gets a truncated PNG. A rename has no such window.
            await File.WriteAllBytesAsync(tempPath, bytes);
            File.Move(tempPath, cachePath, overwrite: true);
        }
        catch
        {
            // Silently fail — icon just won't be shown
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch { }
        }
    }
}
