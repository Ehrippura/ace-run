using System;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace ace_run.Automation;

/// <summary>
/// The things both ends of the automation pipe have to agree on: its name, its framing, and
/// how a line is encoded.
/// </summary>
/// <remarks>
/// In Core because it is shared. The app listens and the MCP server connects, and a name
/// computed twice is a name that can differ once.
/// </remarks>
public static class AutomationPipe
{
    /// <summary>
    /// <c>AceRun.Automation.&lt;user SID&gt;</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The SID is in the name so that two users signed in at once (fast user switching, or a
    /// terminal server) do not collide on one pipe. It also means a connect attempt that lands
    /// on the wrong session fails by name rather than by access check, which is the clearer
    /// failure of the two.
    /// </para>
    /// <para>
    /// Not the user name: names are not unique across domains and can be renamed, and the
    /// server side has to authorize by SID anyway.
    /// </para>
    /// </remarks>
    public static string Name { get; } = $"AceRun.Automation.{CurrentUserSid()}";

    /// <summary>
    /// One line per message, LF-terminated. The reader on each side is a
    /// <c>StreamReader.ReadLine</c>, which accepts CRLF too, so this is about what we write.
    /// </summary>
    public const string LineEnding = "\n";

    /// <summary>
    /// UTF-8 without a byte-order mark. The default <see cref="Encoding.UTF8"/> emits one, and
    /// a BOM at the front of the first line would be part of that line as far as the JSON
    /// reader is concerned.
    /// </summary>
    public static Encoding Encoding { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, AutomationJson.Options);

    /// <summary>Parses a line, or returns null when it is not valid JSON for <typeparamref name="T"/>.</summary>
    /// <remarks>
    /// Null rather than an exception: a line that cannot be read still has to be answered, and
    /// on the other side a server that sends nonsense must not take the client down with it.
    /// </remarks>
    public static T? TryDeserialize<T>(string line) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(line, AutomationJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static string CurrentUserSid()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (identity.User is { } sid) return sid.Value;
        }
        catch (Exception)
        {
            // Falls through to the name below.
        }

        // Only reachable if the process has no Windows identity at all, which on this app
        // cannot happen. The fallback keeps the name well formed rather than throwing from a
        // static initializer, which would be a crash on first use with no useful message.
        return Environment.UserName;
    }
}
