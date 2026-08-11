using System;
using System.Collections.Generic;
using System.Linq;
using ace_run.Models;

namespace ace_run.Services;

/// <summary>
/// How well an item answers the query, best first. Doubles as the "no match" test:
/// <see cref="None"/> means the item is filtered out.
/// </summary>
public enum MatchRank
{
    NamePrefix = 0,
    NameSubstring = 1,
    Tag = 2,
    Path = 3,
    None = 4
}

/// <summary>
/// An item paired with the name of the folder it was found in. The label travels alongside
/// rather than being written onto the item, so ranking stays a function of its inputs.
/// </summary>
public readonly record struct SearchCandidate<T>(T Item, string FolderLabel);

/// <summary>
/// Search matching and result ordering.
/// </summary>
public static class SearchRanking
{
    /// <summary>
    /// Name, path, launch arguments and tag names, all case-insensitive substring. Path is
    /// matched so a URL item is findable by its domain; arguments so two entries pointing at
    /// the same exe can be told apart; tag names so a tag doubles as a query.
    /// Tags are matched one by one rather than against a joined summary string — that string
    /// has a separator in it, so a query could otherwise match across two tag names.
    /// The checks run in rank order and return on the first hit, so a name match never pays
    /// for scanning the path.
    /// Path and arguments share one rank: neither is how a user identifies an item by eye.
    /// </summary>
    public static MatchRank RankOf(IAppItemView app, string query)
    {
        if (app.DisplayName.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return MatchRank.NamePrefix;
        if (app.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
            return MatchRank.NameSubstring;
        if (app.Tags.Any(t => t.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
            return MatchRank.Tag;
        if (app.FilePath.Contains(query, StringComparison.OrdinalIgnoreCase)
            || app.Arguments.Contains(query, StringComparison.OrdinalIgnoreCase))
            return MatchRank.Path;
        return MatchRank.None;
    }

    /// <summary>
    /// Filters <paramref name="candidates"/> to those matching <paramref name="query"/> and
    /// returns them in display order.
    /// </summary>
    /// <param name="recents">
    /// Most recent first — position in this list, not mere membership, is the sort key within
    /// the recent bucket. Pass null or empty when there is no launch history.
    /// </param>
    /// <remarks>
    /// <para>
    /// Recency wins outright: anything the user launched lately sits on top in launch order,
    /// and match quality only sorts the rest. The final key is the order the candidates were
    /// enumerated in — the arrangement the user dragged into place — so equally good hits keep
    /// their familiar sequence.
    /// </para>
    /// <para>
    /// <c>OrderBy</c> and not <c>List.Sort</c>: LINQ's sort is stable and Sort is not, which
    /// would shuffle ties that the explicit order key exists to preserve.
    /// </para>
    /// <para>
    /// An empty query matches everything as a name prefix. Callers that treat "no query" as
    /// "not searching" must not call this.
    /// </para>
    /// </remarks>
    public static List<SearchCandidate<T>> Rank<T>(
        string query,
        IEnumerable<SearchCandidate<T>> candidates,
        IReadOnlyList<RecentLaunch>? recents = null) where T : IAppItemView
    {
        var recentRank = new Dictionary<Guid, int>();
        if (recents is not null)
            for (var i = 0; i < recents.Count; i++)
                recentRank[recents[i].AppId] = i;

        var hits = new List<(SearchCandidate<T> Candidate, int Recent, MatchRank Rank, int Order)>();
        var order = 0;

        foreach (var candidate in candidates)
        {
            var rank = RankOf(candidate.Item, query);
            if (rank == MatchRank.None) continue;

            var recent = recentRank.TryGetValue(candidate.Item.Id, out var r) ? r : int.MaxValue;
            hits.Add((candidate, recent, rank, order++));
        }

        return hits
            .OrderBy(h => h.Recent)
            .ThenBy(h => h.Rank)
            .ThenBy(h => h.Order)
            .Select(h => h.Candidate)
            .ToList();
    }
}
