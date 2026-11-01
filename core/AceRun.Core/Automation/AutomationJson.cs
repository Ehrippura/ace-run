using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ace_run.Automation;

/// <summary>
/// The serializer settings the named pipe runs on.
/// </summary>
/// <remarks>
/// <para>
/// A separate instance from <see cref="Services.AceRunJson"/> rather than the shared one,
/// because that one sets <c>WriteIndented</c>: the framing here is NDJSON — one request or
/// response per line — and indented JSON does not fit on a line. The rest matches it
/// deliberately, so a <see cref="Models.AppData"/> that travels over the pipe and the same
/// object written to disk differ only in whitespace.
/// </para>
/// <para>
/// <see cref="JsonIgnoreCondition.WhenWritingNull"/> is here for size: the request type is one
/// flat record covering every command, so most of its fields are null on any given line.
/// </para>
/// </remarks>
public static class AutomationJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        // Both ends of the pipe write with these same options, so matching cases exactly would
        // work — but the pipe is also driven by hand from a smoke script, and a field whose
        // casing is wrong does not fail, it silently arrives as null. "The command is missing an
        // argument" is a confusing way to be told about a lowercase letter.
        PropertyNameCaseInsensitive = true,

        Converters = { new JsonStringEnumConverter() }
    };
}
