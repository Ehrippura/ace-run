using System.Text.Json;
using ace_run.Models;

namespace ace_run.Services;

/// <summary>Why an <c>.acerun</c> file was refused, or <see cref="None"/> if it was not.</summary>
/// <remarks>
/// An enum rather than a message or a resource key: this layer has no business knowing how the
/// refusal is worded, or in which language.
/// </remarks>
public enum ImportRejection
{
    None,

    /// <summary>Not an AceRun export — unparseable, or parsed into something with no workspace in it.</summary>
    NotAnAceRunFile,

    /// <summary>Written by a build newer than this one, so its contents cannot be trusted.</summary>
    NewerVersion
}

/// <summary>
/// Vetting an <c>.acerun</c> file before it becomes a workspace.
/// </summary>
public static class WorkspaceImport
{
    /// <summary>
    /// Parses and vets the contents of an <c>.acerun</c> file.
    /// </summary>
    /// <param name="export">The workspace to import, or null for any non-<see cref="ImportRejection.None"/> result.</param>
    /// <remarks>
    /// <para>
    /// Parsing and checking are one call because the check needs the raw document, not just the
    /// deserialized object. <see cref="WorkspaceExport.AppData"/> carries a property
    /// initializer, so <c>System.Text.Json</c> leaves a perfectly good empty instance in place
    /// for a file that never mentioned it — which made the old <c>AppData is null</c> guard
    /// almost unreachable, and let any syntactically valid JSON import as a blank workspace.
    /// Requiring the key to actually be there is what makes the rejection mean something.
    /// </para>
    /// <para>
    /// The version check is the other half that was missing. <c>System.Text.Json</c> ignores
    /// keys it has no property for, so a file from a future build imported <em>silently</em>,
    /// dropping whatever that build had added and telling the user nothing.
    /// </para>
    /// <para>
    /// A blank name is deliberately not a rejection: the caller substitutes its own default,
    /// the same as it does for a workspace created by hand.
    /// </para>
    /// </remarks>
    public static ImportRejection TryParse(string? json, out WorkspaceExport? export)
    {
        export = null;

        if (string.IsNullOrWhiteSpace(json))
            return ImportRejection.NotAnAceRunFile;

        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty(nameof(WorkspaceExport.AppData), out var appData)
                || appData.ValueKind != JsonValueKind.Object)
            {
                return ImportRejection.NotAnAceRunFile;
            }
        }
        catch (JsonException)
        {
            return ImportRejection.NotAnAceRunFile;
        }

        WorkspaceExport? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<WorkspaceExport>(json, AceRunJson.Options);
        }
        catch (JsonException)
        {
            return ImportRejection.NotAnAceRunFile;
        }

        if (parsed?.AppData is null)
            return ImportRejection.NotAnAceRunFile;

        if (parsed.AceRunVersion > WorkspaceExport.CurrentVersion)
            return ImportRejection.NewerVersion;

        export = parsed;
        return ImportRejection.None;
    }
}
