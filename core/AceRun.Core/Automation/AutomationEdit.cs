using System;
using System.Collections.Generic;
using System.IO;
using ace_run.Models;
using ace_run.Services;

namespace ace_run.Automation;

/// <summary>
/// The fields an <see cref="AutomationAction.UpdateItem"/> should write, already checked.
/// Null means leave that field as it is.
/// </summary>
public sealed record ItemEdits(
    string? Name,
    string? Target,
    string? Arguments,
    string? WorkingDirectory,
    bool? RunAsAdmin,
    string? SortKey);

/// <summary>
/// Checking an automation write before anything is touched, and building the item it asked for.
/// </summary>
/// <remarks>
/// <para>
/// In Core rather than in the window, so it can be tested without a running app — the whole
/// reason the layer split exists (11-testability.md). The window is then left with the part
/// only it can do: putting the result into the collections the UI is bound to.
/// </para>
/// <para>
/// Everything is validated up front and nothing is half-applied. The caller here is an agent
/// working from a path it may have inferred, and an item whose target does not exist is one the
/// user has to find and delete later, having never asked for it.
/// </para>
/// </remarks>
public static class AutomationEdit
{
    /// <summary>
    /// Builds the item an <see cref="AutomationAction.AddItem"/> describes.
    /// </summary>
    /// <remarks>
    /// Through <see cref="ItemFactory"/> like every other creation path, so a new item gets the
    /// same defaults as one that arrived by drop or picker — most visibly the working directory
    /// for an executable, which is what makes a program that reads files next to itself work.
    /// </remarks>
    public static AutomationError NewItem(AutomationRequest request, out AppItem item)
    {
        item = new AppItem();

        var kindError = ParseKind(request.Kind, out var kind);
        if (kindError != AutomationError.None) return kindError;

        var targetError = ResolveTarget(kind, request.Target, out var target);
        if (targetError != AutomationError.None) return targetError;

        var built = kind == ItemKind.Url ? ItemFactory.FromUrl(target) : ItemFactory.FromPath(target);

        if (request.Name is not null)
        {
            var nameError = ResolveName(request.Name, out var name);
            if (nameError != AutomationError.None) return nameError;
            built.DisplayName = name;
        }

        // An executable named after its file is what the picker produces, and a URL named after
        // its host is what the URL dialog produces. Neither can come out empty here, because
        // the path and the URL have both already been checked — except for a URL whose scheme
        // has no host (ms-settings:), where SuggestDisplayName returns the whole thing.
        if (string.IsNullOrWhiteSpace(built.DisplayName))
            built.DisplayName = target;

        var fieldError = ApplyLaunchFields(built, kind, request);
        if (fieldError != AutomationError.None) return fieldError;

        // Outside ApplyLaunchFields because it is not a launch field: both kinds carry one.
        if (request.SortKey is not null)
            built.SortKey = request.SortKey.Trim();

        item = built;
        return AutomationError.None;
    }

    /// <summary>
    /// The edits an <see cref="AutomationAction.UpdateItem"/> asks for, checked against the kind
    /// of item it is aimed at.
    /// </summary>
    public static AutomationError Edits(AutomationRequest request, ItemKind kind, out ItemEdits edits)
    {
        edits = new ItemEdits(null, null, null, null, null, null);

        // The tool has no kind parameter, so this only fires for a caller driving the pipe
        // directly. Refused rather than ignored: an item is a file item or a URL item for life
        // (7-url-items.md §4), and silently dropping the field would look like it had worked.
        if (request.Kind is not null)
        {
            var kindError = ParseKind(request.Kind, out var asked);
            if (kindError != AutomationError.None) return kindError;
            if (asked != kind) return AutomationError.KindNotEditable;
        }

        string? name = null;
        if (request.Name is not null)
        {
            var nameError = ResolveName(request.Name, out var resolved);
            if (nameError != AutomationError.None) return nameError;
            name = resolved;
        }

        string? target = null;
        if (request.Target is not null)
        {
            var targetError = ResolveTarget(kind, request.Target, out var resolved);
            if (targetError != AutomationError.None) return targetError;
            target = resolved;
        }

        if (kind == ItemKind.Url
            && (request.Arguments is not null || request.WorkingDirectory is not null
                || request.RunAsAdmin is not null))
            return AutomationError.FieldNotApplicable;

        string? workingDirectory = null;
        if (request.WorkingDirectory is not null)
        {
            var dirError = ResolveDirectory(request.WorkingDirectory, out var resolved);
            if (dirError != AutomationError.None) return dirError;
            workingDirectory = resolved;
        }

        // The sort key is trimmed the way the edit dialog trims it, and taken for a URL item
        // as well: the Organize menu sorts both kinds by it (9-organize.md).
        edits = new ItemEdits(
            name, target, request.Arguments, workingDirectory, request.RunAsAdmin,
            request.SortKey?.Trim());

        return AutomationError.None;
    }

    /// <summary>A display name for a folder or a tag.</summary>
    public static AutomationError ResolveName(string? name, out string resolved)
    {
        resolved = name?.Trim() ?? string.Empty;
        return resolved.Length == 0 ? AutomationError.InvalidName : AutomationError.None;
    }

    /// <summary>
    /// Whether something is already called <paramref name="candidate"/>.
    /// </summary>
    /// <remarks>
    /// Without case, in the current culture — the same comparison <c>ManageTagsDialog.IsTaken</c>
    /// makes. It has to be the same one: a rename the dialog refuses must not be accepted here.
    /// The caller leaves the row being renamed out of <paramref name="names"/>, because a tag
    /// does not clash with itself.
    /// </remarks>
    public static bool NameIsTaken(IEnumerable<string> names, string candidate)
    {
        foreach (var name in names)
            if (string.Equals(name, candidate, StringComparison.CurrentCultureIgnoreCase))
                return true;

        return false;
    }

    /// <summary>
    /// A tag colour. An omitted colour is not an error — it is <see cref="ColorKeys.Default"/>,
    /// the same as a tag created in the manage dialog without touching the swatch.
    /// </summary>
    public static AutomationError ResolveColor(string? color, out string key)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            key = ColorKeys.Default;
            return AutomationError.None;
        }

        var trimmed = color.Trim();

        foreach (var known in ColorKeys.All)
            if (string.Equals(known, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                // The stored casing, not the caller's: the key is matched verbatim when the
                // brush is resolved, so "blue" has to become "Blue" on the way in.
                key = known;
                return AutomationError.None;
            }

        key = ColorKeys.Default;
        return AutomationError.InvalidColor;
    }

    private static AutomationError ParseKind(string? kind, out ItemKind parsed)
    {
        parsed = ItemKind.App;

        if (string.IsNullOrWhiteSpace(kind))
            return AutomationError.InvalidKind;

        return Enum.TryParse(kind.Trim(), ignoreCase: true, out parsed) && Enum.IsDefined(parsed)
            ? AutomationError.None
            : AutomationError.InvalidKind;
    }

    /// <summary>
    /// The path or URL to store.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A file is required to exist. The app's own paths come from a picker or a drop and so
    /// always do; this one comes from whatever the caller believes, and the cost of being wrong
    /// lands on the user, who finds an item that does nothing and has no idea where it came
    /// from. The same reasoning is why the read side does <em>not</em> check existence: reporting
    /// what is stored is a different job from accepting something new.
    /// </para>
    /// <para>
    /// A matched pair of surrounding quotes is stripped first. They are not valid in a Windows
    /// filename, so nothing legitimate is lost, and a path pasted out of a command line arrives
    /// wearing them.
    /// </para>
    /// </remarks>
    private static AutomationError ResolveTarget(ItemKind kind, string? target, out string resolved)
    {
        resolved = string.Empty;

        var text = Unquote(target);
        if (text.Length == 0) return AutomationError.InvalidTarget;

        if (kind == ItemKind.Url)
        {
            if (!UrlUtil.TryNormalize(text, out var url)) return AutomationError.InvalidTarget;
            resolved = url;
            return AutomationError.None;
        }

        // Exists rather than a syntax check: a path can be well formed and point at nothing,
        // and that is the case worth refusing. Wrapped because File.Exists is the only call
        // here that touches the filesystem, and an unreachable network path throws rather
        // than returning false.
        try
        {
            if (!File.Exists(text)) return AutomationError.InvalidTarget;
        }
        catch (Exception)
        {
            return AutomationError.InvalidTarget;
        }

        resolved = text;
        return AutomationError.None;
    }

    /// <summary>
    /// A working directory. Empty is allowed and means "clear it", which leaves the launch
    /// using the process default; anything else has to be a directory that exists, for the same
    /// reason the target does.
    /// </summary>
    private static AutomationError ResolveDirectory(string? directory, out string resolved)
    {
        resolved = Unquote(directory);
        if (resolved.Length == 0) return AutomationError.None;

        try
        {
            if (!Directory.Exists(resolved)) return AutomationError.InvalidTarget;
        }
        catch (Exception)
        {
            return AutomationError.InvalidTarget;
        }

        return AutomationError.None;
    }

    private static AutomationError ApplyLaunchFields(AppItem item, ItemKind kind, AutomationRequest request)
    {
        if (kind == ItemKind.Url)
            return request.Arguments is not null || request.WorkingDirectory is not null
                   || request.RunAsAdmin is not null
                ? AutomationError.FieldNotApplicable
                : AutomationError.None;

        if (request.Arguments is not null)
            item.Arguments = request.Arguments;

        if (request.WorkingDirectory is not null)
        {
            var dirError = ResolveDirectory(request.WorkingDirectory, out var directory);
            if (dirError != AutomationError.None) return dirError;
            item.WorkingDirectory = directory;
        }

        if (request.RunAsAdmin is bool admin)
            item.RunAsAdmin = admin;

        return AutomationError.None;
    }

    private static string Unquote(string? text)
    {
        var trimmed = text?.Trim() ?? string.Empty;

        return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"'
            ? trimmed[1..^1].Trim()
            : trimmed;
    }
}
