using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using ace_run.Models;

namespace ace_run.Automation;

/// <summary>
/// What an automation caller asks the running app to do.
/// </summary>
/// <remarks>
/// <see cref="Unknown"/> is the zero value and is never sent. It is what an action name this
/// build does not recognize resolves to, which is how a newer MCP server talking to an older
/// app gets <see cref="AutomationError.UnknownCommand"/> back instead of a dropped connection.
/// </remarks>
public enum AutomationAction
{
    Unknown = 0,

    /// <summary>
    /// The live configuration and the live contents of the active workspace. The one action
    /// that changes nothing, and so the only one a read-only installation answers.
    /// </summary>
    ReadState,

    LaunchItem,
    AddItem,
    UpdateItem,
    DeleteItem,
    MoveItem,
    CreateFolder,
    RenameFolder,
    DeleteFolder,
    SetItemTags,
    CreateTag,
    UpdateTag,
    DeleteTag,
    SwitchWorkspace,
    ShowWindow,
    HideWindow,
    ClearRecentLaunches
}

/// <summary>
/// Why a request was refused, as a value rather than a sentence.
/// </summary>
/// <remarks>
/// <para>
/// The app side cannot write the message: <c>AceRun.Core</c> carries no localization, and the
/// reader is an MCP client rather than the user, so the wording belongs to the server that
/// speaks to that client. Same split as <c>WorkspaceImport.TryParse</c>.
/// </para>
/// <para>
/// Every member is a distinct thing the caller can do differently. Folding "no such folder"
/// into "no such item" would leave the server unable to say which name to go and look up.
/// </para>
/// </remarks>
public enum AutomationError
{
    None = 0,

    UnknownCommand,
    MissingArgument,

    /// <summary>
    /// Automation is switched off. Normally unreachable, since the pipe is not listening in
    /// that state, but a request already in flight when the user switches it off lands here.
    /// </summary>
    Disabled,

    /// <summary>Automation is set to read-only and this request would do something.</summary>
    ReadOnly,

    /// <summary>
    /// A dialog or flyout is open in the app. Automation never opens one of its own, but the
    /// user may have one up, and the collections it is editing are the ones a write would move
    /// underneath it.
    /// </summary>
    Busy,

    WorkspaceNotFound,
    WorkspaceAmbiguous,

    /// <summary>
    /// The request named a workspace that is not the one the app has open. Writes and launches
    /// go through the live window, and the window holds exactly one workspace.
    /// </summary>
    WorkspaceNotActive,

    FolderNotFound,
    FolderAmbiguous,
    ItemNotFound,
    ItemAmbiguous,
    TagNotFound,
    TagAmbiguous,

    /// <summary>
    /// Another tag already answers to that name. The manage dialog refuses the same rename,
    /// and a clash is unreachable through the UI — a new row is given a uniquified default
    /// name — so accepting one here would build a state the window cannot, and leave every
    /// later request that names the tag answering "ambiguous".
    /// </summary>
    TagNameInUse,

    /// <summary>A name that is empty or only whitespace.</summary>
    InvalidName,

    /// <summary>No file at that path, or a URL that will not launch.</summary>
    InvalidTarget,

    /// <summary>A colour key that is not one of <see cref="Services.ColorKeys.All"/>.</summary>
    InvalidColor,

    /// <summary>A kind that is neither <c>App</c> nor <c>Url</c>.</summary>
    InvalidKind,

    /// <summary>An attempt to turn a file item into a URL item or back (7-url-items.md §4).</summary>
    KindNotEditable,

    /// <summary>
    /// A field that means nothing for this kind of item: arguments, working directory or
    /// administrator mode on a URL item, which launches through the shell and ignores all
    /// three. Storing it would describe a launch that cannot happen.
    /// </summary>
    FieldNotApplicable,

    /// <summary>
    /// The item runs as administrator and the separate opt-in for that is off. A launch from
    /// here would raise a UAC prompt the user never asked for.
    /// </summary>
    AdminLaunchNotAllowed,

    /// <summary>
    /// Hiding the window was refused because nothing would bring it back: no tray icon and no
    /// global hotkey.
    /// </summary>
    NoWayToRestore,

    /// <summary>The OS refused to start the process; <c>Detail</c> carries what it said.</summary>
    LaunchFailed,

    Internal
}

/// <summary>
/// One automation request, as it travels over the pipe.
/// </summary>
/// <remarks>
/// <para>
/// One flat record for every command rather than a type per command. The alternative is
/// <see cref="JsonDerivedTypeAttribute"/> on fifteen types that mostly carry one or two
/// strings, and what it would buy is the serializer rejecting a field the command has no use
/// for — while the thing that actually has to be checked, that the fields it <em>does</em>
/// need are present, still needs <see cref="Validate"/>. So the checking is in one place and
/// the shape stays plain.
/// </para>
/// <para>
/// A null field means "not given". For the fields <see cref="AutomationAction.UpdateItem"/>
/// can change, that is the difference between leaving a value alone and clearing it: null
/// leaves it, an empty string clears it.
/// </para>
/// </remarks>
public sealed record AutomationRequest
{
    /// <summary>Echoed back on the response, so a caller can match them up.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// The <see cref="AutomationAction"/> name. A string on the wire, not the enum: a member
    /// this build has never heard of makes <c>System.Text.Json</c> throw on the whole line, and
    /// then there is no id left to answer with.
    /// </summary>
    public string Command { get; init; } = string.Empty;

    /// <summary>
    /// The workspace the caller believes it is addressing — an assertion rather than a
    /// selector. A request naming anything but the open workspace is refused with
    /// <see cref="AutomationError.WorkspaceNotActive"/>; only
    /// <see cref="AutomationAction.SwitchWorkspace"/> reads it as a destination.
    /// </summary>
    public string? Workspace { get; init; }

    /// <summary>An item id or name.</summary>
    public string? Item { get; init; }

    /// <summary>
    /// A folder id or name. On <see cref="AutomationAction.RenameFolder"/> and
    /// <see cref="AutomationAction.DeleteFolder"/> it is the subject and is required; on
    /// <see cref="AutomationAction.AddItem"/> and <see cref="AutomationAction.MoveItem"/> it is
    /// the destination, and leaving it out means ungrouped.
    /// </summary>
    public string? Folder { get; init; }

    /// <summary>A display name: the item's, the folder's, or the tag's.</summary>
    public string? Name { get; init; }

    /// <summary>The executable path, or the URL.</summary>
    public string? Target { get; init; }

    public string? Arguments { get; init; }
    public string? WorkingDirectory { get; init; }
    public bool? RunAsAdmin { get; init; }

    /// <summary>
    /// The free-form ordering key the Organize menu reads. Not a launch field: a URL item has
    /// one too, which is why the edit dialog keeps its box visible for both kinds.
    /// </summary>
    public string? SortKey { get; init; }

    /// <summary>
    /// <see cref="ItemKind"/> by name, for <see cref="AutomationAction.AddItem"/> only.
    /// Required there, and deliberately not inferred from the target: the kind cannot be
    /// changed afterwards, so a wrong guess is permanent.
    /// </summary>
    public string? Kind { get; init; }

    /// <summary>
    /// The tag an <see cref="AutomationAction.UpdateTag"/> or
    /// <see cref="AutomationAction.DeleteTag"/> acts on, by id or name. Separate from
    /// <see cref="Tags"/>, which never names a subject — it is the list an item's tags are
    /// replaced with.
    /// </summary>
    public string? Tag { get; init; }

    /// <summary>
    /// Tag ids or names, replacing whatever the item carries. On
    /// <see cref="AutomationAction.SetItemTags"/> an empty list clears them and null is a
    /// missing argument; on <see cref="AutomationAction.AddItem"/> null just means the new item
    /// starts with none.
    /// </summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>A colour key from <see cref="Services.ColorKeys.All"/>.</summary>
    public string? Color { get; init; }

    /// <summary><see cref="Command"/> resolved, or <see cref="AutomationAction.Unknown"/>.</summary>
    /// <remarks>
    /// The leading-letter test is there because <c>Enum.TryParse</c> also accepts the
    /// underlying numbers, which would let "4" arrive as whichever action happens to be fourth
    /// today.
    /// </remarks>
    [JsonIgnore]
    public AutomationAction Action =>
        Command.Length > 0 && char.IsAsciiLetter(Command[0])
        && Enum.TryParse<AutomationAction>(Command, ignoreCase: true, out var action)
            ? action
            : AutomationAction.Unknown;

    /// <summary>
    /// False only for <see cref="AutomationAction.ReadState"/>. Showing and hiding the window
    /// count as doing something: the read-only setting means "look, do not touch", and a window
    /// appearing on the user's screen is a touch.
    /// </summary>
    [JsonIgnore]
    public bool ChangesSomething => Action != AutomationAction.ReadState;

    /// <summary>The arguments this command cannot run without.</summary>
    public AutomationError Validate()
    {
        switch (Action)
        {
            case AutomationAction.Unknown:
                return AutomationError.UnknownCommand;

            case AutomationAction.ReadState:
            case AutomationAction.ShowWindow:
            case AutomationAction.HideWindow:
            case AutomationAction.ClearRecentLaunches:
                return AutomationError.None;

            case AutomationAction.LaunchItem:
            case AutomationAction.DeleteItem:
            case AutomationAction.MoveItem:
                return Given(Item);

            case AutomationAction.AddItem:
                return Given(Target, Kind);

            case AutomationAction.UpdateItem:
                var missing = Given(Item);
                if (missing != AutomationError.None) return missing;
                // An update that changes nothing is a mistake worth reporting, not a no-op to
                // confirm: the caller meant to set something and left it out.
                return ChangesAField ? AutomationError.None : AutomationError.MissingArgument;

            case AutomationAction.CreateFolder:
            case AutomationAction.CreateTag:
                return Given(Name);

            case AutomationAction.RenameFolder:
                return Given(Folder, Name);

            case AutomationAction.DeleteFolder:
                return Given(Folder);

            case AutomationAction.SetItemTags:
                return Tags is null ? AutomationError.MissingArgument : Given(Item);

            case AutomationAction.UpdateTag:
                var noTag = Given(Tag);
                if (noTag != AutomationError.None) return noTag;
                // The rule UpdateItem has, for the reason UpdateItem has it.
                return Name is not null || Color is not null
                    ? AutomationError.None
                    : AutomationError.MissingArgument;

            case AutomationAction.DeleteTag:
                return Given(Tag);

            case AutomationAction.SwitchWorkspace:
                return Given(Workspace);

            default:
                return AutomationError.Internal;
        }
    }

    private bool ChangesAField =>
        Name is not null || Target is not null || Arguments is not null
        || WorkingDirectory is not null || RunAsAdmin is not null || SortKey is not null;

    private static AutomationError Given(params string?[] values)
    {
        foreach (var value in values)
            if (string.IsNullOrWhiteSpace(value))
                return AutomationError.MissingArgument;

        return AutomationError.None;
    }
}

/// <summary>
/// What the app is holding right now: the configuration it will write back, and the contents of
/// the one workspace it has open.
/// </summary>
/// <remarks>
/// <para>
/// Only the active workspace travels, because only the active workspace is live. The others are
/// files the app is not holding, and the MCP server can read those itself — asking the app to go
/// and read them would add a hop and answer no better.
/// </para>
/// <para>
/// This is what closes the gap the read-only tools had to disclaim: with text in the search box
/// the app holds edits back from disk (<c>SaveItems</c> returns early), and the workspace in
/// that state is by definition the one the user is working in — this one.
/// </para>
/// </remarks>
public sealed record AutomationState
{
    public required WorkspaceConfig Config { get; init; }

    /// <summary>The workspace <see cref="ActiveData"/> belongs to.</summary>
    public required Guid ActiveWorkspaceId { get; init; }

    public required AppData ActiveData { get; init; }

    /// <summary>Lets a caller tell "writes are switched off" from "the app is not running".</summary>
    public AutomationMode Mode { get; init; }
}

/// <summary>One answer, carrying whichever of the payloads the command produces.</summary>
public sealed record AutomationResponse
{
    public required string Id { get; init; }

    public AutomationError Error { get; init; }

    /// <summary>What an ambiguous name matched, so the caller can be told what to choose between.</summary>
    public IReadOnlyList<LookupCandidate>? Candidates { get; init; }

    /// <summary>
    /// Diagnostic text for the failures that are not ours to enumerate — what the OS said when a
    /// launch failed. Not a message for the user, and never the only thing a caller is told.
    /// </summary>
    public string? Detail { get; init; }

    public AutomationState? State { get; init; }
    public ItemSummary? Item { get; init; }
    public FolderSummary? Folder { get; init; }
    public TagSummary? Tag { get; init; }
    public WorkspaceSummary? Workspace { get; init; }

    /// <summary>How many of something the command affected, where that is the answer.</summary>
    public int? Count { get; init; }

    /// <summary>JsonIgnore: a get-only property would otherwise be written on every line.</summary>
    [JsonIgnore]
    public bool Ok => Error == AutomationError.None;

    public static AutomationResponse Done(string id) => new() { Id = id };

    public static AutomationResponse Failed(
        string id,
        AutomationError error,
        IReadOnlyList<LookupCandidate>? candidates = null,
        string? detail = null) =>
        new() { Id = id, Error = error, Candidates = candidates, Detail = detail };
}
