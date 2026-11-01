using System.Collections.Generic;
using System.ComponentModel;
using ace_run.Automation;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace ace_run.Mcp.Tools;

/// <summary>
/// The read-only half of the automation interface.
/// </summary>
/// <remarks>
/// Every tool here changes nothing, which is why they all carry <c>ReadOnly</c> and
/// <c>Idempotent</c>: a client is free to call them without asking the user first. The ones
/// that act are in <see cref="ActionTools"/> and do not share that property.
///
/// Each call opens its own <see cref="WorkspaceAccess"/>, which asks the running app for its
/// live state and falls back to the data files. That is why these answers are current even for
/// edits the app has not written yet, and why they still arrive when it is closed.
///
/// The methods are thin on purpose. Resolution and failure messages belong to
/// <see cref="WorkspaceAccess"/> and the projections to <see cref="AutomationQuery"/>, so what
/// is left here is the protocol surface: the tool's name, what it is for, and what its
/// parameters mean.
/// </remarks>
[McpServerToolType]
internal static class QueryTools
{
    private const string WorkspaceParam =
        "Workspace id or name. Omit for the active workspace, or pass 'all' for every workspace "
        + "(each row says which one it came from). A name is matched without case, exactly first, "
        + "then as a substring; a name matching several workspaces is an error that lists them.";

    private const string PagingNote =
        "Returns a page: 'total' is how many items matched, 'items' is this page, and "
        + "'nextOffset' is what to pass as offset for the rest — do not compute that yourself, "
        + "and when it is absent this page is the last one. When total is much larger than the "
        + "page, narrowing with tag or folder is better than paging through everything.";

    [McpServerTool(Name = "list_workspaces", Title = "List workspaces", ReadOnly = true, Idempotent = true)]
    [Description("Lists Ace Run's workspaces with their ids, colour tags, item counts, and which one is active.")]
    public static IReadOnlyList<WorkspaceSummary> ListWorkspaces() =>
        AutomationQuery.Workspaces(WorkspaceAccess.Open().Config);

    [McpServerTool(Name = "list_folders", Title = "List folders", ReadOnly = true, Idempotent = true)]
    [Description("Lists the folders in a workspace. Items that are not in a folder are not reported here; list_items returns them with no folder.")]
    public static IReadOnlyList<FolderSummary> ListFolders(
        [Description(WorkspaceParam)] string? workspace = null)
    {
        var folders = new List<FolderSummary>();

        foreach (var loaded in WorkspaceAccess.Open().Resolve(workspace))
            folders.AddRange(AutomationQuery.Folders(loaded.Data, loaded.Info.Name));

        return folders;
    }

    [McpServerTool(Name = "list_items", Title = "List launch items", ReadOnly = true, Idempotent = true)]
    [Description("Lists launch items: name, kind (App or Url), target path or URL, arguments, administrator flag, tags, folder and workspace. Narrow with tag and folder; " + PagingNote)]
    public static ItemPage ListItems(
        [Description(WorkspaceParam)] string? workspace = null,
        [Description("Folder id or name, matched like the workspace parameter. Omit for every item in the workspace, each naming its own folder. Needs a single workspace.")]
        string? folder = null,
        [Description("Tag id or name. Only items carrying that tag are returned. Tags belong to a workspace, so with workspace 'all' the ones that have no such tag are skipped; a tag no workspace has is an error.")]
        string? tag = null,
        [Description("Items per page. Default 50, maximum 100.")] int? limit = null,
        [Description("Items to skip, for the next page.")] int offset = 0)
    {
        var items = new List<ItemSummary>();
        var source = WorkspaceAccess.Open();

        if (folder is not null)
        {
            var one = source.ResolveOne(workspace, "folder");
            var tagged = WorkspaceAccess.WithTag(new[] { one }, tag);

            foreach (var (loaded, tagId) in tagged)
                items.AddRange(AutomationQuery.Items(
                    loaded.Data, loaded.Info.Name, WorkspaceAccess.Folder(loaded, folder), tagId));
        }
        else
        {
            foreach (var (loaded, tagId) in WorkspaceAccess.WithTag(source.Resolve(workspace), tag))
                items.AddRange(AutomationQuery.Items(loaded.Data, loaded.Info.Name, null, tagId));
        }

        return AutomationQuery.Page(items, offset, limit);
    }

    [McpServerTool(Name = "search_items", Title = "Search launch items", ReadOnly = true, Idempotent = true)]
    [Description("Searches a workspace the way the app's own search box does — by name, target path, arguments and tag name — and returns the matches in the same order, with recently launched items first. To list everything, or to filter by one tag exactly, use list_items. " + PagingNote)]
    public static ItemPage SearchItems(
        [Description("What to look for. Matched without case; an empty query is rejected.")] string query,
        [Description(WorkspaceParam)] string? workspace = null,
        [Description("Items per page. Default 50, maximum 100.")] int? limit = null,
        [Description("Items to skip, for the next page.")] int offset = 0)
    {
        // Checked here as well as in AutomationQuery.Search, because only an McpException's
        // message reaches the caller — anything else is reported as "an error occurred", which
        // tells an agent nothing about what to do differently. The guard in Core stays: it is
        // that method's contract, and this one is the protocol's error message.
        if (string.IsNullOrWhiteSpace(query))
            throw new McpException("A search needs a query. Call list_items to list everything instead.");

        var hits = new List<ItemSummary>();

        foreach (var loaded in WorkspaceAccess.Open().Resolve(workspace))
            hits.AddRange(AutomationQuery.Search(loaded.Data, loaded.Info.Name, query));

        return AutomationQuery.Page(hits, offset, limit);
    }

    [McpServerTool(Name = "list_tags", Title = "List tags", ReadOnly = true, Idempotent = true)]
    [Description("Lists tags with their ids, colour keys and workspace. Tags belong to a workspace, not to the app, so pass workspace 'all' to find which workspace a tag lives in.")]
    public static IReadOnlyList<TagSummary> ListTags(
        [Description(WorkspaceParam)] string? workspace = null)
    {
        var tags = new List<TagSummary>();

        foreach (var loaded in WorkspaceAccess.Open().Resolve(workspace))
            tags.AddRange(AutomationQuery.Tags(loaded.Data, loaded.Info.Name));

        return tags;
    }

    [McpServerTool(Name = "get_recent_launches", Title = "Get recent launches", ReadOnly = true, Idempotent = true)]
    [Description("Lists what was launched from a workspace lately, most recent first. The name is the one the item had when it ran.")]
    public static IReadOnlyList<RecentSummary> GetRecentLaunches(
        [Description(WorkspaceParam)] string? workspace = null)
    {
        var recents = new List<RecentSummary>();

        foreach (var loaded in WorkspaceAccess.Open().Resolve(workspace))
            recents.AddRange(AutomationQuery.Recents(loaded.Data, loaded.Info.Name));

        return recents;
    }
}
