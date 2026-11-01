using System;
using System.Collections.Generic;
using System.Linq;
using ace_run.Automation;
using ace_run.Models;
using ModelContextProtocol;

namespace ace_run.Mcp;

/// <summary>
/// Gets the tools the data they are about to project, and turns everything that can go wrong
/// into a message the caller can act on.
/// </summary>
/// <remarks>
/// <para>
/// One instance per tool call, from <see cref="Open"/>. It asks the running app for its live
/// state first and falls back to the files on disk — so a query answers either way, and when the
/// app <em>is</em> up the answer includes edits it has not written yet. That last part is the gap
/// the read-only stage had to disclaim in its tool descriptions: with text in the search box the
/// app holds changes back from disk, and the workspace in that state is the one it has open.
/// </para>
/// <para>
/// Only the open workspace comes from the app, because only the open workspace is live. Any
/// other is a file the app is not holding, and reading it here is both correct and one hop
/// shorter.
/// </para>
/// <para>
/// The failures below are the ones an agent can recover from by itself — a name that matched two
/// workspaces, a name that matched none, a tag that lives in a different workspace — so each
/// message says what to call next rather than only what went wrong.
/// </para>
/// <para>
/// English, like the rest of the protocol surface. The app's own UI is localized through
/// <c>Loc</c>; this text is read by a client, not by the user, and <c>AceRun.Core</c> is
/// deliberately free of localization (which is also why the lookup layer returns enums).
/// </para>
/// </remarks>
internal sealed class WorkspaceAccess
{
    private static readonly AutomationReader Reader = AutomationReader.Default;

    /// <summary>What a caller passes as the workspace to mean "every one of them".</summary>
    public const string AllKeyword = "all";

    /// <summary>One workspace's index entry and its loaded contents.</summary>
    public readonly record struct Loaded(WorkspaceInfo Info, AppData Data);

    private readonly AutomationState? _live;

    private WorkspaceAccess(AutomationState? live, WorkspaceConfig config)
    {
        _live = live;
        Config = config;
    }

    public WorkspaceConfig Config { get; }

    /// <summary>True when this call is reading the running app rather than the files.</summary>
    public bool Live => _live is not null;

    public static WorkspaceAccess Open()
    {
        var live = AutomationClient.TryReadState();

        if (live is null)
            return new WorkspaceAccess(null, ReadConfig());

        // The stored count is denormalized and only updated on save, so for the one workspace
        // whose real contents we are holding, report what is actually in it. The config here is
        // our own deserialized copy, so correcting it affects nothing but this answer.
        foreach (var info in live.Config.Workspaces)
            if (info.Id == live.ActiveWorkspaceId)
                info.AppCount = live.ActiveData.ItemCount;

        return new WorkspaceAccess(live, live.Config);
    }

    private static WorkspaceConfig ReadConfig()
    {
        var result = Reader.ReadConfig();

        if (result.Ok) return result.Value!;

        throw new McpException(result.Outcome switch
        {
            ReadOutcome.Missing =>
                $"Ace Run has no configuration yet ({Reader.Paths.ConfigFile} does not exist). "
                + "It is written the first time the app runs.",
            ReadOutcome.Unreadable =>
                $"Could not read {Reader.Paths.ConfigFile}. Something else is holding it open.",
            _ =>
                $"{Reader.Paths.ConfigFile} is not valid Ace Run configuration."
        });
    }

    /// <summary>
    /// The workspaces a key names: one by id or name, the open one when it is empty, or all of
    /// them for <see cref="AllKeyword"/>.
    /// </summary>
    /// <remarks>
    /// The keyword is only considered once name resolution has failed, so a workspace actually
    /// called "All" still wins and stays reachable by its name.
    /// </remarks>
    public IReadOnlyList<Loaded> Resolve(string? key)
    {
        var found = ItemLookup.Workspace(Config, key);

        if (found.Outcome == LookupOutcome.Ambiguous)
            throw new McpException(
                $"'{key}' matches {found.Candidates.Count} workspaces ({Describe(found.Candidates)}). "
                + "Pass one of those ids.");

        if (found.Value is not null)
            return new[] { new Loaded(found.Value, Load(found.Value)) };

        if (IsAll(key))
            return LoadAll();

        throw new McpException(string.IsNullOrWhiteSpace(key)
            ? "The configuration lists no workspaces."
            : $"No workspace matches '{key}'. Call list_workspaces to see them, "
              + $"or pass '{AllKeyword}' for every workspace.");
    }

    /// <summary>
    /// The one workspace a key names, for a question that cannot span several —
    /// <paramref name="because"/> names the parameter that makes it so.
    /// </summary>
    public Loaded ResolveOne(string? key, string because)
    {
        if (IsAll(key))
            throw new McpException(
                $"'{because}' applies to a single workspace, so workspace cannot be '{AllKeyword}' here. "
                + "Name the workspace.");

        return Resolve(key)[0];
    }

    /// <summary>
    /// The workspaces to actually query, each with the tag id to filter it by.
    /// </summary>
    /// <remarks>
    /// Tags live per workspace, so one name can resolve in some and not in others; a workspace
    /// where it does not resolve is dropped from the result rather than carried with a null
    /// filter, which to <see cref="AutomationQuery.Items"/> would mean "every item". Only if it
    /// resolves nowhere is that an error — and then the message says where to look.
    /// </remarks>
    public static List<(Loaded Workspace, Guid? TagId)> WithTag(IReadOnlyList<Loaded> workspaces, string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return workspaces.Select(w => (w, (Guid?)null)).ToList();

        var matched = new List<(Loaded, Guid?)>();

        foreach (var workspace in workspaces)
        {
            var found = ItemLookup.Tag(workspace.Data, key);

            if (found.Outcome == LookupOutcome.Ambiguous)
                throw new McpException(
                    $"'{key}' matches {found.Candidates.Count} tags in workspace "
                    + $"'{workspace.Info.Name}' ({Describe(found.Candidates)}). Pass one of those ids.");

            if (found.Value is not null)
                matched.Add((workspace, found.Value.Id));
        }

        if (matched.Count == 0)
            throw new McpException(workspaces.Count == 1
                ? $"Workspace '{workspaces[0].Info.Name}' has no tag matching '{key}'. "
                  + $"Tags are per workspace — call list_tags with workspace '{AllKeyword}' to see them all."
                : $"No workspace has a tag matching '{key}'. Call list_tags with workspace '{AllKeyword}'.");

        return matched;
    }

    /// <summary>The folder a key names inside one workspace, or null for the whole workspace.</summary>
    public static FolderItem? Folder(Loaded workspace, string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;

        var found = ItemLookup.Folder(workspace.Data, key);

        if (found.Outcome == LookupOutcome.Ambiguous)
            throw new McpException(
                $"'{key}' matches {found.Candidates.Count} folders ({Describe(found.Candidates)}). "
                + "Pass one of those ids.");

        if (found.Value is null)
            throw new McpException(
                $"No folder matches '{key}' in workspace '{workspace.Info.Name}'. Call list_folders to see them.");

        return found.Value;
    }

    private static bool IsAll(string? key) =>
        string.Equals(key?.Trim(), AllKeyword, StringComparison.OrdinalIgnoreCase);

    private AppData Load(WorkspaceInfo info)
    {
        if (_live is not null && info.Id == _live.ActiveWorkspaceId)
            return _live.ActiveData;

        var data = Reader.ReadWorkspace(info.Id);

        if (data.Ok) return data.Value!;

        throw new McpException(data.Outcome switch
        {
            // The config lists it but its file is gone: a hand-edited data directory, or a
            // workspace created and never saved. Reporting it beats answering "it is empty".
            ReadOutcome.Missing =>
                $"Workspace '{info.Name}' is listed in the configuration but its data file is missing.",
            ReadOutcome.Unreadable =>
                $"Could not read workspace '{info.Name}'. Something else is holding its file open.",
            _ =>
                $"Workspace '{info.Name}' has a data file that is not valid JSON."
        });
    }

    /// <remarks>
    /// A workspace with no file yet is skipped rather than fatal — it has nothing to contribute
    /// to an across-everything answer. One that exists and cannot be read or parsed still
    /// throws: silently leaving its items out would make this a confident wrong answer.
    /// </remarks>
    private IReadOnlyList<Loaded> LoadAll()
    {
        var loaded = new List<Loaded>(Config.Workspaces.Count);

        foreach (var info in Config.Workspaces)
        {
            if (_live is not null && info.Id == _live.ActiveWorkspaceId)
            {
                loaded.Add(new Loaded(info, _live.ActiveData));
                continue;
            }

            var data = Reader.ReadWorkspace(info.Id);

            if (data.Outcome == ReadOutcome.Missing) continue;

            loaded.Add(new Loaded(info, data.Ok ? data.Value! : Load(info)));
        }

        if (loaded.Count == 0)
            throw new McpException("No workspace has any data on disk yet.");

        return loaded;
    }

    private static string Describe(IReadOnlyList<LookupCandidate> candidates) =>
        string.Join(", ", candidates.Select(c => $"{c.Name} = {c.Id}"));
}
