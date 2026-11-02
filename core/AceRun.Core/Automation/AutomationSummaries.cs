using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ace_run.Automation;

/// <summary>
/// What the automation interface says about a workspace.
/// </summary>
/// <param name="ItemCount">
/// The denormalized <see cref="Models.WorkspaceInfo.AppCount"/>, which is what the workspace
/// switcher shows. Counting the real thing would mean opening every workspace file to answer
/// one question about the list.
/// </param>
public sealed record WorkspaceSummary(Guid Id, string Name, string? Color, int ItemCount, bool IsActive);

/// <param name="Workspace">
/// Which workspace this came from. Carried on every row because a caller may have asked across
/// all of them, and a folder or tag name alone does not say where it lives.
/// </param>
public sealed record FolderSummary(Guid Id, string Name, int ItemCount, string Workspace);

public sealed record TagSummary(Guid Id, string Name, string Color, string Workspace);

/// <summary>One entry of the launch history: the item's id, and what it was called when it ran.</summary>
public sealed record RecentSummary(Guid Id, string Name, string Workspace);

/// <summary>
/// A page of items, with the size of the whole result beside it.
/// </summary>
/// <remarks>
/// A bare list would have been the obvious shape and it was the wrong one: a workspace here has
/// 349 items, and a client that receives all of them in one tool result spools it to a file
/// instead of reading it — which is exactly what drove an agent to go and parse the data files
/// by hand. <see cref="Total"/> exists so the caller can tell "that is everything" from "there
/// is more", and answer by narrowing the query rather than paging through it.
/// </remarks>
/// <param name="Total">Matching items before paging.</param>
/// <param name="Offset">Where this page starts.</param>
/// <param name="Items">This page.</param>
/// <param name="NextOffset">
/// What to pass as the next <c>offset</c>, or null when this page is the last one.
/// <para>
/// Computed here rather than left to the caller because the caller got it wrong: an agent
/// paging through 110 items reported folder counts one too high, which is what an overlapping
/// page looks like when the second request starts one row early. Arithmetic the server can do
/// exactly is not worth asking a language model to redo.
/// </para>
/// </param>
public sealed record ItemPage(
    int Total,
    int Offset,
    IReadOnlyList<ItemSummary> Items,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? NextOffset);

/// <summary>
/// What the automation interface says about a launch item.
/// </summary>
/// <remarks>
/// A projection rather than the <see cref="Models.AppItem"/> itself, so that reporting an item
/// cannot come to mean publishing every field a later version adds. What it leaves out is
/// <c>CustomIconPath</c>, which says little without the icon cache it belongs to. <c>SortKey</c>
/// was left out on that same reasoning and should not have been: the user types it into the edit
/// dialog by hand, and the Organize menu has an entry that reads it.
///
/// Every nullable field is omitted from the JSON rather than serialized as null. On a listing
/// of a few hundred items those nulls are a real fraction of the payload, and the payload is
/// what has to fit in a client's context.
/// </remarks>
public sealed record ItemSummary
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    /// <summary>"App" or "Url" — the same vocabulary the workspace file uses.</summary>
    public required string Kind { get; init; }

    /// <summary>The executable's path, or the URL. Reported as stored; not resolved, not checked.</summary>
    public required string Target { get; init; }

    /// <summary>Null for a URL item, which ignores it (see 7-url-items.md §5).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Arguments { get; init; }

    /// <summary>
    /// Null for a URL item, and omitted when it is simply the target's own folder — which is
    /// what <see cref="Services.ItemFactory.FromPath"/> sets and so what most items have.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkingDirectory { get; init; }

    /// <summary>Omitted unless true, which is the rare case.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool RunAsAdmin { get; init; }

    /// <summary>
    /// The user's ordering key, which only the Organize menu reads. Omitted when it is empty,
    /// which is most items.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SortKey { get; init; }

    /// <summary>Tag names in workspace tag order. Omitted when the item has none.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>The folder's name. Omitted when the item is ungrouped.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Folder { get; init; }

    public required string Workspace { get; init; }
}
