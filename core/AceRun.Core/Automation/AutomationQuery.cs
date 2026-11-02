using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ace_run.Models;
using ace_run.Services;

namespace ace_run.Automation;

/// <summary>
/// Turns a loaded workspace into the automation interface's answers.
/// </summary>
/// <remarks>
/// Search goes through <see cref="SearchRanking"/> rather than re-implementing matching, so an
/// agent and the search box agree on both which items hit and what order they come back in.
/// That is the whole reason this lives in Core next to the ranking it borrows.
/// </remarks>
public static class AutomationQuery
{
    /// <summary>
    /// How many items a page holds when the caller does not say.
    /// </summary>
    /// <remarks>
    /// Small on purpose, and the number is measured rather than chosen: on a real library of
    /// Japanese titles a row serializes to roughly 220 bytes, so 50 rows is about 11 KB, 100
    /// about 23 KB, and the whole 349-item workspace about 81 KB. Clients spool a tool result
    /// they consider too large to a file and hand the model a short preview instead — which is
    /// what made an agent abandon these tools and go parse the data files by hand. The default
    /// is what protects against that, because the caller that would have to pass a limit is the
    /// one already in trouble.
    /// </remarks>
    public const int DefaultLimit = 50;

    /// <summary>
    /// The most a single page may hold, however large a limit is asked for. Deliberately only
    /// twice the default: beyond this the honest answer is to narrow with <c>tag</c> or
    /// <c>folder</c>, not to ask for a page no client will read.
    /// </summary>
    public const int MaxLimit = 100;

    public static List<WorkspaceSummary> Workspaces(WorkspaceConfig config) =>
        config.Workspaces
            .Select(w => new WorkspaceSummary(w.Id, w.Name, w.ColorTag, w.AppCount, w.Id == config.ActiveWorkspaceId))
            .ToList();

    public static List<FolderSummary> Folders(AppData data, string workspace) =>
        data.Folders.Select(f => new FolderSummary(f.Id, f.DisplayName, f.Children.Count, workspace)).ToList();

    public static List<TagSummary> Tags(AppData data, string workspace) =>
        data.Tags.Select(t => new TagSummary(t.Id, t.Name, t.ColorKey, workspace)).ToList();

    public static List<RecentSummary> Recents(AppData data, string workspace) =>
        data.RecentLaunches.Select(r => new RecentSummary(r.AppId, r.DisplayName, workspace)).ToList();

    /// <summary>
    /// The items in <paramref name="folder"/> — or every item in the workspace when it is null —
    /// optionally narrowed to one tag.
    /// </summary>
    /// <remarks>
    /// "Every item" is flat and ungrouped-first, with each entry naming its own folder. The app
    /// shows one folder at a time because a person reads one screen at a time; a caller asking
    /// over a protocol is better served by the whole workspace in one answer.
    /// </remarks>
    public static List<ItemSummary> Items(
        AppData data,
        string workspace,
        FolderItem? folder = null,
        Guid? tagId = null)
    {
        var items = new List<ItemSummary>();

        if (folder is not null)
        {
            foreach (var item in folder.Children)
                if (HasTag(item, tagId))
                    items.Add(Describe(data, item, folder.DisplayName, workspace));

            return items;
        }

        foreach (var item in data.UngroupedItems)
            if (HasTag(item, tagId))
                items.Add(Describe(data, item, null, workspace));

        foreach (var f in data.Folders)
            foreach (var item in f.Children)
                if (HasTag(item, tagId))
                    items.Add(Describe(data, item, f.DisplayName, workspace));

        return items;
    }

    /// <summary>
    /// Matching items, best first, by the same rules as the search box — including recency,
    /// which outranks match quality.
    /// </summary>
    /// <remarks>
    /// An empty query is the caller's mistake, not "everything": <see cref="SearchRanking.Rank"/>
    /// treats it as a name prefix that matches every item, and its own remarks say callers who
    /// read "no query" as "not searching" must not call it. Listing is a different question with
    /// a different answer, so this refuses instead of quietly becoming <see cref="Items"/>.
    /// </remarks>
    public static List<ItemSummary> Search(AppData data, string workspace, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("A search needs a query.", nameof(query));

        return SearchRanking.Rank(query, Candidates(data), data.RecentLaunches)
            .Select(hit => Describe(data, hit.Item.Source, Label(hit.FolderLabel), workspace))
            .ToList();
    }

    /// <summary>
    /// One page of <paramref name="items"/>, with the total beside it so the caller can tell
    /// whether narrowing the query would serve it better than asking for the next page.
    /// </summary>
    public static ItemPage Page(IReadOnlyList<ItemSummary> items, int offset = 0, int? limit = null)
    {
        var start = Math.Clamp(offset, 0, items.Count);
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var page = items.Skip(start).Take(take).ToList();
        var next = start + page.Count;

        return new ItemPage(items.Count, start, page, next < items.Count ? next : null);
    }

    /// <summary>
    /// The folder <paramref name="itemId"/> sits in, or null when it is ungrouped.
    /// </summary>
    /// <remarks>
    /// For the write side, which resolves an item by name and then has to say where it ended
    /// up. The read side never needs it: it walks the folders anyway and already knows.
    /// </remarks>
    public static FolderItem? FolderOf(AppData data, Guid itemId)
    {
        foreach (var folder in data.Folders)
            foreach (var item in folder.Children)
                if (item.Id == itemId) return folder;

        return null;
    }

    private static bool HasTag(AppItem item, Guid? tagId) =>
        tagId is null || item.TagIds.Contains(tagId.Value);

    /// <summary>
    /// Ungrouped first, then each folder in order — the order
    /// <c>MainWindow.SearchCandidates</c> enumerates, which <see cref="SearchRanking.Rank"/>
    /// uses as its final tie-break. Matching it is what makes two equally good hits come back
    /// in the sequence the user dragged them into.
    /// </summary>
    private static IEnumerable<SearchCandidate<ItemView>> Candidates(AppData data)
    {
        // The window labels this group with a localized string. Core does not localize, and the
        // projection reports "no folder" as null anyway, so the label is left empty here.
        foreach (var item in data.UngroupedItems)
            yield return new SearchCandidate<ItemView>(ViewOf(data, item), string.Empty);

        foreach (var folder in data.Folders)
            foreach (var item in folder.Children)
                yield return new SearchCandidate<ItemView>(ViewOf(data, item), folder.DisplayName);
    }

    private static string? Label(string folderLabel) =>
        folderLabel.Length == 0 ? null : folderLabel;

    private static ItemView ViewOf(AppData data, AppItem item) =>
        new(item, TagOrdering.InWorkspaceOrder(data.Tags, item.TagIds));

    /// <summary>
    /// One item as the automation interface reports it.
    /// </summary>
    /// <remarks>
    /// Public because the write side answers with it too: a command that launched, added or
    /// moved something reports the item it acted on, through this same projection, so a client
    /// never has to reconcile two descriptions of the same row.
    /// </remarks>
    public static ItemSummary Describe(AppData data, AppItem item, string? folder, string workspace)
    {
        var isApp = item.Kind == ItemKind.App;
        var tags = TagOrdering.InWorkspaceOrder(data.Tags, item.TagIds);

        return new ItemSummary
        {
            Id = item.Id,
            Name = item.DisplayName,
            Kind = item.Kind.ToString(),
            Target = item.FilePath,
            Arguments = isApp ? Text(item.Arguments) : null,
            WorkingDirectory = isApp ? WorkingDirectory(item) : null,
            RunAsAdmin = isApp && item.RunAsAdmin,
            SortKey = Text(item.SortKey),
            Tags = tags.Count == 0 ? null : tags.Select(t => t.Name).ToList(),
            Folder = folder,
            Workspace = workspace
        };
    }

    /// <summary>
    /// The working directory, unless it is the target's own folder.
    /// </summary>
    /// <remarks>
    /// That is what <see cref="ItemFactory.FromPath"/> sets, so on a listing it is a near-copy
    /// of <c>Target</c> on nearly every row — pure weight in the answer, and the caller can
    /// derive it. A directory the user actually chose is different information and is reported.
    /// </remarks>
    private static string? WorkingDirectory(AppItem item)
    {
        var dir = Text(item.WorkingDirectory);
        if (dir is null) return null;

        // No guard around GetDirectoryName: on .NET it is string manipulation and does not
        // validate characters, which a test pins — a stored path can be anything the user
        // typed, and one odd row must not fail a whole listing.
        var targetDir = Path.GetDirectoryName(item.FilePath);

        return !string.IsNullOrEmpty(targetDir) && string.Equals(
            dir.TrimEnd(Path.DirectorySeparatorChar),
            targetDir.TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase)
            ? null
            : dir;
    }

    /// <summary>Empty becomes null: an unset field is worth reporting as absent, not as "".</summary>
    private static string? Text(string value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>
    /// An <see cref="AppItem"/> seen as the search layer wants it, with its tag ids resolved to
    /// the workspace's tags.
    /// </summary>
    /// <remarks>
    /// <see cref="IAppItemView"/> exists for the app's view models, which hold resolved tag
    /// objects; a stored <see cref="AppItem"/> holds ids and so cannot implement it directly.
    /// <see cref="TagOrdering.InWorkspaceOrder"/> does the resolution, which also drops ids of
    /// deleted tags and keeps the workspace's tag order — the invariant the interface documents.
    /// </remarks>
    private sealed class ItemView : IAppItemView
    {
        private readonly IReadOnlyList<TagItem> _tags;

        public ItemView(AppItem source, IReadOnlyList<TagItem> tags)
        {
            Source = source;
            _tags = tags;
        }

        public AppItem Source { get; }

        public Guid Id => Source.Id;
        public string DisplayName => Source.DisplayName;
        public string FilePath => Source.FilePath;
        public string Arguments => Source.Arguments;
        public string SortKey => Source.SortKey;
        public IEnumerable<ITagRef> Tags => _tags;
    }
}
