using System;
using System.Collections.Generic;
using System.Linq;
using ace_run.Models;

namespace ace_run.Automation;

public enum LookupOutcome
{
    Found,
    NotFound,

    /// <summary>The key named more than one thing. <c>Candidates</c> says which.</summary>
    Ambiguous
}

/// <summary>One of the things an ambiguous key matched, so the caller can be told what to pick between.</summary>
public sealed record LookupCandidate(Guid Id, string Name);

public readonly record struct LookupResult<T>(
    LookupOutcome Outcome,
    T? Value,
    IReadOnlyList<LookupCandidate> Candidates) where T : class;

/// <summary>
/// Resolving the workspace or folder an automation caller named, by id or by name.
/// </summary>
/// <remarks>
/// <para>
/// The rules are spelled out rather than left to <c>FirstOrDefault</c> because the caller is
/// an agent working from names it read a moment ago, and the cost of guessing is not symmetric:
/// a wrong pick here silently answers a question about the wrong workspace, and the same
/// resolution will later decide which program gets launched. Two names that both match is
/// therefore an answer of its own, not a tie to break.
/// </para>
/// <para>
/// A key that parses as a <see cref="Guid"/> is only ever matched against ids. Falling back to
/// name matching for an id that is not there would turn a precise request into a fuzzy one.
/// </para>
/// </remarks>
public static class ItemLookup
{
    private static readonly IReadOnlyList<LookupCandidate> None = Array.Empty<LookupCandidate>();

    /// <summary>
    /// The workspace <paramref name="key"/> names, or the active one when it is empty.
    /// </summary>
    /// <remarks>
    /// A stale <see cref="WorkspaceConfig.ActiveWorkspaceId"/> falls back to the first
    /// workspace rather than failing. <c>DataStore.MigrateOrInitialize</c> repairs that index
    /// at the app's next start, and the repair is a <em>write</em> — which this side must never
    /// do. Refusing would turn a question we can still answer into an error.
    /// </remarks>
    public static LookupResult<WorkspaceInfo> Workspace(WorkspaceConfig config, string? key)
    {
        if (config.Workspaces.Count == 0)
            return new LookupResult<WorkspaceInfo>(LookupOutcome.NotFound, null, None);

        if (string.IsNullOrWhiteSpace(key))
        {
            var active = config.Workspaces.FirstOrDefault(w => w.Id == config.ActiveWorkspaceId)
                         ?? config.Workspaces[0];
            return new LookupResult<WorkspaceInfo>(LookupOutcome.Found, active, None);
        }

        return Resolve(config.Workspaces, key, w => w.Id, w => w.Name);
    }

    public static LookupResult<FolderItem> Folder(AppData data, string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return new LookupResult<FolderItem>(LookupOutcome.NotFound, null, None);

        return Resolve(data.Folders, key, f => f.Id, f => f.DisplayName);
    }

    /// <summary>
    /// The tag <paramref name="key"/> names. Tags live per workspace, so the same name can
    /// resolve in one workspace and not in another — a caller looking for a tag it saw
    /// elsewhere has to say where.
    /// </summary>
    public static LookupResult<TagItem> Tag(AppData data, string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return new LookupResult<TagItem>(LookupOutcome.NotFound, null, None);

        return Resolve(data.Tags, key, t => t.Id, t => t.Name);
    }

    private static LookupResult<T> Resolve<T>(
        IReadOnlyList<T> items,
        string key,
        Func<T, Guid> idOf,
        Func<T, string> nameOf) where T : class
    {
        key = key.Trim();

        if (Guid.TryParse(key, out var id))
        {
            var byId = items.FirstOrDefault(i => idOf(i) == id);
            return byId is null
                ? new LookupResult<T>(LookupOutcome.NotFound, null, None)
                : new LookupResult<T>(LookupOutcome.Found, byId, None);
        }

        // Exact first, so an item whose whole name is another's prefix stays reachable: with
        // "Work" and "Work (old)" present, "Work" is not ambiguous.
        var matches = items
            .Where(i => string.Equals(nameOf(i), key, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
            matches = items
                .Where(i => nameOf(i).Contains(key, StringComparison.OrdinalIgnoreCase))
                .ToList();

        return matches.Count switch
        {
            1 => new LookupResult<T>(LookupOutcome.Found, matches[0], None),
            0 => new LookupResult<T>(LookupOutcome.NotFound, null, None),
            _ => new LookupResult<T>(
                LookupOutcome.Ambiguous,
                null,
                matches.Select(i => new LookupCandidate(idOf(i), nameOf(i))).ToList())
        };
    }
}
