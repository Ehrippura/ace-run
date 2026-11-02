using System.Collections.Generic;
using System.ComponentModel;
using ace_run.Automation;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace ace_run.Mcp.Tools;

/// <summary>
/// The half of the automation interface that does something.
/// </summary>
/// <remarks>
/// <para>
/// Every tool here goes through the running app over the named pipe, and none of them is
/// <c>ReadOnly</c> or <c>Idempotent</c>. Writes cannot go to the files directly: the app holds
/// the live workspace in its view models and rewrites the whole file from them on the next save,
/// so an edit made behind its back would be overwritten (12-mcp-automation.md, opening section).
/// </para>
/// <para>
/// There is no confirmation step on this side. A destructive tool is marked
/// <c>Destructive</c> and the client is expected to ask its user; the app deliberately does not
/// put a dialog in the way, because nobody is looking at the screen when a command arrives.
/// </para>
/// <para>
/// They act on the workspace the app has open, and only that one — the window holds exactly one.
/// Naming a different workspace is an error that says to call <c>switch_workspace</c> first,
/// rather than a switch made silently under the user.
/// </para>
/// </remarks>
[McpServerToolType]
internal static class ActionTools
{
    private const string ActiveOnly =
        "Acts on the workspace Ace Run currently has open. Optional, and checked rather than "
        + "obeyed: pass it to be told if the app has a different workspace open instead of "
        + "acting on the wrong one. Use switch_workspace to change which that is.";

    private const string ItemParam =
        "Item id or name. A name is matched without case, exactly first, then as a substring; a "
        + "name matching several items is an error that lists their ids, because picking one "
        + "would mean acting on the wrong item.";

    private const string TagParam =
        "Tag id or name, matched the way the item parameter is: without case, exactly first and "
        + "then as a substring, and a name matching several tags is an error listing their ids.";

    private const string SortKeyParam =
        "Sort key: a free-form string the app orders items by when the user picks Organize → "
        + "By Sort Key. Nothing else reads it, no tile shows it, and search does not match it. "
        + "An empty string clears it.";

    [McpServerTool(Name = "launch_item", Title = "Launch an item", Destructive = false)]
    [Description("Starts a launch item through Ace Run, with its arguments, working directory and administrator setting — which is what makes a launch behave as it does from the window. Returns the item that was actually resolved, so the caller can confirm which one ran. Starts Ace Run first if it is not running.")]
    public static ItemSummary LaunchItem(
        [Description(ItemParam)] string item,
        [Description(ActiveOnly)] string? workspace = null) =>
        Item(new AutomationRequest
        {
            Command = nameof(AutomationAction.LaunchItem),
            Item = item,
            Workspace = workspace
        });

    [McpServerTool(Name = "add_item", Title = "Add an item", Destructive = false)]
    [Description("Adds a launch item to the open workspace. An executable path has to exist and a URL has to be launchable, both checked before anything is created. 'kind' is required and cannot be changed afterwards.")]
    public static ItemSummary AddItem(
        [Description("'App' for an executable, or 'Url' for a URL or custom protocol (http, steam, mailto, ms-settings).")]
        string kind,
        [Description("The executable's full path, or the URL. A bare host gets https:// put in front of it.")]
        string target,
        [Description("Display name. Omit for the file's name without its extension, or a URL's host — what the app itself would have used.")]
        string? name = null,
        [Description("Folder id or name to put it in. Omit to leave it ungrouped. Which folder the user happens to have open does not come into it.")]
        string? folder = null,
        [Description("Command-line arguments. Not accepted for a URL item, which ignores them.")]
        string? arguments = null,
        [Description("Working directory. Omit to use the executable's own folder, which is what the app does.")]
        string? workingDirectory = null,
        [Description("Run elevated. Launching such an item from automation needs a separate opt-in in Ace Run's settings.")]
        bool? runAsAdmin = null,
        [Description("Tag ids or names to put on it. Each one has to exist already — create_tag first. Omit for an untagged item.")]
        IReadOnlyList<string>? tags = null,
        [Description(SortKeyParam)] string? sortKey = null,
        [Description(ActiveOnly)] string? workspace = null) =>
        Item(new AutomationRequest
        {
            Command = nameof(AutomationAction.AddItem),
            Kind = kind,
            Target = target,
            Name = name,
            Folder = folder,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RunAsAdmin = runAsAdmin,
            Tags = tags,
            SortKey = sortKey,
            Workspace = workspace
        });

    [McpServerTool(Name = "update_item", Title = "Change an item", Destructive = false)]
    [Description("Changes fields on an existing item. Omitted fields are left alone; an empty string clears one. The kind cannot be changed, and a URL item accepts none of arguments, working directory or administrator mode.")]
    public static ItemSummary UpdateItem(
        [Description(ItemParam)] string item,
        [Description("New display name.")] string? name = null,
        [Description("New executable path or URL. Checked the same way add_item checks it.")] string? target = null,
        [Description("New arguments; an empty string clears them.")] string? arguments = null,
        [Description("New working directory; an empty string clears it, leaving the process default.")]
        string? workingDirectory = null,
        [Description("Whether to run elevated.")] bool? runAsAdmin = null,
        [Description(SortKeyParam)] string? sortKey = null,
        [Description(ActiveOnly)] string? workspace = null) =>
        Item(new AutomationRequest
        {
            Command = nameof(AutomationAction.UpdateItem),
            Item = item,
            Name = name,
            Target = target,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RunAsAdmin = runAsAdmin,
            SortKey = sortKey,
            Workspace = workspace
        });

    [McpServerTool(Name = "delete_item", Title = "Delete an item", Destructive = true)]
    [Description("Removes an item from the open workspace, along with its cached icon and its launch history entry. There is no undo and no confirmation prompt in the app — ask the user first. Returns the item that was deleted.")]
    public static ItemSummary DeleteItem(
        [Description(ItemParam)] string item,
        [Description(ActiveOnly)] string? workspace = null) =>
        Item(new AutomationRequest
        {
            Command = nameof(AutomationAction.DeleteItem),
            Item = item,
            Workspace = workspace
        });

    [McpServerTool(Name = "move_item", Title = "Move an item", Destructive = false)]
    [Description("Moves an item into a folder, or out of every folder when no folder is given. The item keeps its id, tags and launch history.")]
    public static ItemSummary MoveItem(
        [Description(ItemParam)] string item,
        [Description("Destination folder id or name. Omit to move the item out of its folder, leaving it ungrouped.")]
        string? folder = null,
        [Description(ActiveOnly)] string? workspace = null) =>
        Item(new AutomationRequest
        {
            Command = nameof(AutomationAction.MoveItem),
            Item = item,
            Folder = folder,
            Workspace = workspace
        });

    [McpServerTool(Name = "set_item_tags", Title = "Set an item's tags", Destructive = false)]
    [Description("Replaces an item's tags with the ones given — this is not an add, so pass the full set, and an empty list clears them. Every tag has to exist already; use create_tag first. Tags come back in the workspace's own tag order, which is what keeps them consistent across items.")]
    public static ItemSummary SetItemTags(
        [Description(ItemParam)] string item,
        [Description("Tag ids or names, matched like the item parameter. An empty list removes every tag from the item.")]
        IReadOnlyList<string> tags,
        [Description(ActiveOnly)] string? workspace = null) =>
        Item(new AutomationRequest
        {
            Command = nameof(AutomationAction.SetItemTags),
            Item = item,
            Tags = tags,
            Workspace = workspace
        });

    [McpServerTool(Name = "create_folder", Title = "Create a folder", Destructive = false)]
    [Description("Adds an empty folder to the open workspace. Duplicate names are allowed, as they are in the app, but an item addressed by a duplicated folder name will come back ambiguous.")]
    public static FolderSummary CreateFolder(
        [Description("The folder's name.")] string name,
        [Description(ActiveOnly)] string? workspace = null) =>
        Folder(new AutomationRequest
        {
            Command = nameof(AutomationAction.CreateFolder),
            Name = name,
            Workspace = workspace
        });

    [McpServerTool(Name = "rename_folder", Title = "Rename a folder", Destructive = false)]
    [Description("Renames a folder. The items in it are untouched.")]
    public static FolderSummary RenameFolder(
        [Description("Folder id or name.")] string folder,
        [Description("The new name.")] string name,
        [Description(ActiveOnly)] string? workspace = null) =>
        Folder(new AutomationRequest
        {
            Command = nameof(AutomationAction.RenameFolder),
            Folder = folder,
            Name = name,
            Workspace = workspace
        });

    [McpServerTool(Name = "delete_folder", Title = "Delete a folder", Destructive = true)]
    [Description("Deletes a folder AND every item inside it — the items are not moved out first. There is no undo and no confirmation prompt in the app, so ask the user, and consider move_item for anything worth keeping. Returns the folder that was deleted, with the number of items that went with it.")]
    public static FolderSummary DeleteFolder(
        [Description("Folder id or name.")] string folder,
        [Description(ActiveOnly)] string? workspace = null) =>
        Folder(new AutomationRequest
        {
            Command = nameof(AutomationAction.DeleteFolder),
            Folder = folder,
            Workspace = workspace
        });

    [McpServerTool(Name = "create_tag", Title = "Create a tag", Destructive = false)]
    [Description("Adds a tag to the open workspace. Tags belong to a workspace, not to the app. Assign it with set_item_tags, or name it in add_item. A name another tag in the workspace already has is refused, as it is in the app's manage dialog.")]
    public static TagSummary CreateTag(
        [Description("The tag's name.")] string name,
        [Description("Colour: Blue, Green, Red, Yellow, Purple or Gray. Omit for Blue.")] string? color = null,
        [Description(ActiveOnly)] string? workspace = null)
    {
        var response = AutomationClient.Send(new AutomationRequest
        {
            Command = nameof(AutomationAction.CreateTag),
            Name = name,
            Color = color,
            Workspace = workspace
        });

        return response.Tag ?? throw Empty();
    }

    [McpServerTool(Name = "update_tag", Title = "Rename or recolour a tag", Destructive = false)]
    [Description("Renames a tag, changes its colour, or both. Every item carrying it follows, because items reference the tag rather than copying it. A name another tag in the workspace already has is refused.")]
    public static TagSummary UpdateTag(
        [Description(TagParam)] string tag,
        [Description("New name. Omit to leave it as it is.")] string? name = null,
        [Description("New colour: Blue, Green, Red, Yellow, Purple or Gray. Omit to leave it as it is.")]
        string? color = null,
        [Description(ActiveOnly)] string? workspace = null)
    {
        var response = AutomationClient.Send(new AutomationRequest
        {
            Command = nameof(AutomationAction.UpdateTag),
            Tag = tag,
            Name = name,
            Color = color,
            Workspace = workspace
        });

        return response.Tag ?? throw Empty();
    }

    [McpServerTool(Name = "delete_tag", Title = "Delete a tag", Destructive = true)]
    [Description("Removes a tag from the open workspace and takes it off every item that carried it; the items themselves stay. There is no undo and the app asks nothing, so ask the user first — list_items with that tag says how many items it is on.")]
    public static string DeleteTag(
        [Description(TagParam)] string tag,
        [Description(ActiveOnly)] string? workspace = null)
    {
        var response = AutomationClient.Send(new AutomationRequest
        {
            Command = nameof(AutomationAction.DeleteTag),
            Tag = tag,
            Workspace = workspace
        });

        var deleted = response.Tag ?? throw Empty();

        return $"Deleted the tag '{deleted.Name}'; it was on {response.Count ?? 0} items.";
    }

    [McpServerTool(Name = "switch_workspace", Title = "Switch workspace", Destructive = false)]
    [Description("Opens a different workspace in Ace Run, which is what the other action tools then act on. This is a visible change to what the user is looking at. Already being open is not an error.")]
    public static WorkspaceSummary SwitchWorkspace(
        [Description("Workspace id or name, matched without case, exactly first and then as a substring.")]
        string workspace)
    {
        var response = AutomationClient.Send(new AutomationRequest
        {
            Command = nameof(AutomationAction.SwitchWorkspace),
            Workspace = workspace
        });

        return response.Workspace ?? throw Empty();
    }

    [McpServerTool(Name = "show_window", Title = "Show the window", Destructive = false)]
    [Description("Brings Ace Run's window to the front, starting it if it is not running. Only worth calling when the user asked to see it.")]
    public static string ShowWindow()
    {
        AutomationClient.Send(new AutomationRequest { Command = nameof(AutomationAction.ShowWindow) });
        return "Ace Run is in the foreground.";
    }

    [McpServerTool(Name = "hide_window", Title = "Hide the window", Destructive = false)]
    [Description("Hides Ace Run's window, leaving it running in the system tray. Refused when there is neither a tray icon nor a global hotkey, since then nothing could bring it back.")]
    public static string HideWindow()
    {
        AutomationClient.Send(new AutomationRequest { Command = nameof(AutomationAction.HideWindow) });
        return "Ace Run is hidden; it is still running in the tray.";
    }

    [McpServerTool(Name = "clear_recent_launches", Title = "Clear the launch history", Destructive = true)]
    [Description("Empties the open workspace's recent-launch list, which is also the tray menu and a ranking input for search_items. The items themselves are untouched.")]
    public static string ClearRecentLaunches(
        [Description(ActiveOnly)] string? workspace = null)
    {
        var response = AutomationClient.Send(new AutomationRequest
        {
            Command = nameof(AutomationAction.ClearRecentLaunches),
            Workspace = workspace
        });

        return $"Cleared {response.Count ?? 0} recent launches.";
    }

    private static ItemSummary Item(AutomationRequest request) =>
        AutomationClient.Send(request).Item ?? throw Empty();

    private static FolderSummary Folder(AutomationRequest request) =>
        AutomationClient.Send(request).Folder ?? throw Empty();

    /// <summary>
    /// A success with no payload, which means the two sides disagree about the shape of an
    /// answer — a mismatched pair of builds. Worth its own message rather than a null
    /// dereference, because the fix is to line the versions up.
    /// </summary>
    private static McpException Empty() =>
        new("Ace Run reported success but sent no details back. The app and this server may be "
            + "different versions.");
}
