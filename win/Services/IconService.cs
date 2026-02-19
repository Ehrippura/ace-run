using System;
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

    public static async Task<BitmapImage?> GetIconAsync(string filePath, Guid itemId, string? customIconPath = null)
    {
        var iconSource = !string.IsNullOrEmpty(customIconPath) && File.Exists(customIconPath)
            ? customIconPath
            : filePath;

        if (!File.Exists(iconSource))
            return null;

        var cachePath = Path.Combine(CacheDir, $"{itemId:N}.png");

        if (!File.Exists(cachePath))
            await ExtractAndCacheIconAsync(iconSource, cachePath);

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

    public static void InvalidateCache(Guid itemId)
    {
        var cachePath = Path.Combine(CacheDir, $"{itemId:N}.png");
        try { if (File.Exists(cachePath)) File.Delete(cachePath); }
        catch { }
    }

    private static async Task ExtractAndCacheIconAsync(string filePath, string cachePath)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);

            var storageFile = await StorageFile.GetFileFromPathAsync(filePath);
            using var thumbnail = await storageFile.GetThumbnailAsync(
                ThumbnailMode.SingleItem, 32, ThumbnailOptions.UseCurrentScale);

            if (thumbnail == null || thumbnail.Size == 0)
                return;

            var size = (uint)thumbnail.Size;
            var dataReader = new DataReader(thumbnail.GetInputStreamAt(0));
            await dataReader.LoadAsync(size);
            var bytes = new byte[size];
            dataReader.ReadBytes(bytes);
            dataReader.DetachStream();

            await File.WriteAllBytesAsync(cachePath, bytes);
        }
        catch
        {
            // Silently fail — icon just won't be shown
        }
    }
}
