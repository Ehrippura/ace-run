using System;
using System.Collections.Generic;
using System.Linq;
using ace_run.Models;

namespace ace_run.Services;

/// <summary>What a merge did, for the message the caller shows afterwards.</summary>
/// <remarks>
/// Counts rather than a sentence: this layer has no business knowing how the outcome is
/// worded, or in which language — the same reason <see cref="ImportRejection"/> is an enum.
/// </remarks>
public readonly record struct MergeResult(
    int ItemsAdded,
    int FoldersCreated,
    int FoldersMerged,
    int TagsCreated);

/// <summary>
/// Folding an imported workspace into an existing one.
/// </summary>
/// <remarks>
/// <para>
/// Merging is purely additive: nothing in the destination is renamed, recoloured, reordered or
/// removed. Two things are matched by name rather than duplicated — folders and tags — because
/// those are the only names the user sees repeated, and a second "Games" folder or a second
/// "Work" tag is indistinguishable from the first in the UI. Items are never matched: two
/// entries pointing at the same .exe are a legitimate thing to keep (different arguments,
/// different working directory), so collapsing them would destroy data the user meant to have.
/// </para>
/// <para>
/// Every imported item and folder is rebuilt under a <em>fresh</em> id, and the incoming ids
/// are thrown away. Ids are not unique across files: exporting a workspace and merging it back
/// — or merging the export of a workspace that was created with "copy current" — hands us ids
/// the destination already uses. Icon cache entries are keyed by <see cref="AppItem.Id"/> and
/// nothing on disk remembers which workspace an id belongs to, so a collision would make two
/// items share one cache entry, and deleting either would blank the other's icon. Fresh ids
/// cost the imported items their cached icons, which are re-extracted on next load.
/// </para>
/// </remarks>
public static class WorkspaceMerge
{
    /// <summary>
    /// Merges <paramref name="incoming"/> into <paramref name="target"/>, which is mutated in
    /// place. <paramref name="incoming"/> is only read.
    /// </summary>
    public static MergeResult Merge(AppData target, AppData incoming)
    {
        // A file that says "Tags": null deserializes to a null list — the property
        // initializers only cover a key that was absent — and an .acerun file is user-supplied.
        target.Tags ??= new List<TagItem>();
        target.UngroupedItems ??= new List<AppItem>();
        target.Folders ??= new List<FolderItem>();
        target.RecentLaunches ??= new List<RecentLaunch>();

        var tagMap = MapTags(target, incoming.Tags, out var tagsCreated);

        // Old id -> new id, for the recent-launch entries that reference them.
        var itemMap = new Dictionary<Guid, Guid>();
        var itemsAdded = 0;

        foreach (var item in incoming.UngroupedItems ?? Enumerable.Empty<AppItem>())
        {
            target.UngroupedItems.Add(Adopt(item, target.Tags, tagMap, itemMap));
            itemsAdded++;
        }

        var foldersCreated = 0;
        var foldersMerged = 0;

        foreach (var folder in incoming.Folders ?? Enumerable.Empty<FolderItem>())
        {
            // Matched against the result list, so two incoming folders sharing a name land in
            // one folder as well. They are already indistinguishable to the user, and the
            // alternative is a merge that leaves behind exactly the duplicate it set out to
            // avoid.
            var destination = target.Folders.FirstOrDefault(f => NameEquals(f.DisplayName, folder.DisplayName));

            if (destination is null)
            {
                destination = new FolderItem { DisplayName = folder.DisplayName };
                target.Folders.Add(destination);
                foldersCreated++;
            }
            else
            {
                foldersMerged++;
            }

            foreach (var item in folder.Children ?? Enumerable.Empty<AppItem>())
            {
                destination.Children.Add(Adopt(item, target.Tags, tagMap, itemMap));
                itemsAdded++;
            }
        }

        MergeRecents(target.RecentLaunches, incoming.RecentLaunches, itemMap);

        return new MergeResult(itemsAdded, foldersCreated, foldersMerged, tagsCreated);
    }

    /// <summary>
    /// Maps each incoming tag id onto the destination tag it should mean, adding the tags the
    /// destination does not have.
    /// </summary>
    /// <remarks>
    /// An existing tag keeps its colour: the destination workspace's palette is the user's, and
    /// an import is not a reason to repaint it. A new tag keeps the colour it arrived with but
    /// not its id, for the same reason items do not keep theirs.
    /// </remarks>
    private static Dictionary<Guid, Guid> MapTags(AppData target, List<TagItem>? incoming, out int created)
    {
        var map = new Dictionary<Guid, Guid>();
        created = 0;

        foreach (var tag in incoming ?? Enumerable.Empty<TagItem>())
        {
            var existing = target.Tags.FirstOrDefault(t => NameEquals(t.Name, tag.Name));

            if (existing is null)
            {
                existing = new TagItem { Name = tag.Name, ColorKey = tag.ColorKey };
                target.Tags.Add(existing);
                created++;
            }

            map[tag.Id] = existing.Id;
        }

        return map;
    }

    /// <summary>A copy of <paramref name="source"/> under a new id, with its tags re-pointed.</summary>
    private static AppItem Adopt(
        AppItem source,
        List<TagItem> targetTags,
        Dictionary<Guid, Guid> tagMap,
        Dictionary<Guid, Guid> itemMap)
    {
        var mapped = new HashSet<Guid>();
        foreach (var id in source.TagIds ?? Enumerable.Empty<Guid>())
        {
            if (tagMap.TryGetValue(id, out var destinationId))
                mapped.Add(destinationId);
        }

        var copy = new AppItem
        {
            Kind = source.Kind,
            DisplayName = source.DisplayName,
            FilePath = source.FilePath,
            Arguments = source.Arguments,
            WorkingDirectory = source.WorkingDirectory,
            RunAsAdmin = source.RunAsAdmin,
            CustomIconPath = source.CustomIconPath,
            SortKey = source.SortKey,

            // Through the workspace list, which is what puts them in workspace order and drops
            // ids that mapped to nothing — the invariant the tag dots on every tile rely on.
            TagIds = TagOrdering.InWorkspaceOrder(targetTags, mapped).Select(t => t.Id).ToList()
        };

        // Indexer, not Add: a hand-edited file can name the same id twice, and a merge is not
        // the place to throw over it.
        itemMap[source.Id] = copy.Id;
        return copy;
    }

    /// <summary>
    /// Appends the imported recents after the destination's own, then trims to the cap.
    /// </summary>
    /// <remarks>
    /// The destination's entries stay in front: they are this user's actual launch history on
    /// this machine, and the imported ones are a record of launches that happened somewhere
    /// else. An entry whose item did not come across is dropped — the list is keyed by item id
    /// and search reads the position as a rank, so a dangling entry would rank nothing.
    /// </remarks>
    private static void MergeRecents(
        List<RecentLaunch> target,
        List<RecentLaunch>? incoming,
        Dictionary<Guid, Guid> itemMap)
    {
        foreach (var recent in incoming ?? Enumerable.Empty<RecentLaunch>())
        {
            if (!itemMap.TryGetValue(recent.AppId, out var newId)) continue;

            target.Add(new RecentLaunch
            {
                AppId = newId,
                DisplayName = recent.DisplayName,
                FilePath = recent.FilePath
            });
        }

        if (target.Count > RecentLaunchList.MaxRecent)
            target.RemoveRange(RecentLaunchList.MaxRecent, target.Count - RecentLaunchList.MaxRecent);
    }

    /// <summary>
    /// Folder and tag name matching. Trimmed and case-insensitive, matching the rule the
    /// workspace name check already uses — "Games" and "games " are one name to the user.
    /// </summary>
    private static bool NameEquals(string? a, string? b) =>
        string.Equals((a ?? string.Empty).Trim(), (b ?? string.Empty).Trim(),
            StringComparison.CurrentCultureIgnoreCase);
}
