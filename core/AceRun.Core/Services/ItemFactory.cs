using System.Collections.Generic;
using System.IO;
using System.Linq;
using ace_run.Models;

namespace ace_run.Services;

/// <summary>
/// Builds a new <see cref="AppItem"/> from the one thing a drop or a picker gives us.
/// </summary>
public static class ItemFactory
{
    /// <summary>An executable: named after the file, rooted in its own folder.</summary>
    public static AppItem FromPath(string filePath) => new()
    {
        DisplayName = Path.GetFileNameWithoutExtension(filePath),
        FilePath = filePath,
        WorkingDirectory = Path.GetDirectoryName(filePath) ?? string.Empty
    };

    /// <summary>
    /// A URL or custom protocol.
    /// </summary>
    /// <remarks>
    /// No WorkingDirectory: <see cref="Path.GetDirectoryName(string)"/> on a URL yields junk
    /// like <c>https:\example.com</c>. An empty url is the "add a URL" dialog's starting
    /// state, and gets an empty name rather than a suggested one.
    /// </remarks>
    public static AppItem FromUrl(string url) => new()
    {
        Kind = ItemKind.Url,
        DisplayName = url.Length > 0 ? UrlUtil.SuggestDisplayName(url) : string.Empty,
        FilePath = url
    };
}

/// <summary>
/// Reading across a whole workspace, where ungrouped and foldered items are the same thing to
/// the caller.
/// </summary>
public static class AppDataQuery
{
    /// <summary>Every item in the workspace, ungrouped first, then each folder's children.</summary>
    public static IEnumerable<AppItem> AllItems(AppData data)
        => data.UngroupedItems.Concat(data.Folders.SelectMany(f => f.Children));

    /// <summary>
    /// Every item id in a workspace. Icon cache entries are keyed by id and nothing on disk
    /// remembers which workspace an id belonged to, so this is what a workspace deletion reads
    /// out of the file before removing it.
    /// </summary>
    public static IEnumerable<System.Guid> ItemIds(AppData data) => AllItems(data).Select(i => i.Id);
}
