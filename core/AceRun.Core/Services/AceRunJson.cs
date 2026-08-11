using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ace_run.Services;

/// <summary>
/// The one <see cref="JsonSerializerOptions"/> every AceRun file goes through, including the
/// <c>.acerun</c> import/export in ManageWorkspacesDialog — sharing one instance is what stops
/// the two from drifting.
/// </summary>
/// <remarks>
/// This lives apart from <see cref="DataStore"/> so that reading the options cannot drag a
/// path layer in with it. It used to be a property on the old static <c>DataService</c>, whose
/// type initializer resolved <c>%LOCALAPPDATA%</c> and created the data directory — meaning a
/// caller that only wanted to serialize a string still touched the disk.
/// </remarks>
public static class AceRunJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,

        // Enums persist as readable names rather than numbers: ItemKind, AppTheme, and the
        // hotkey's VirtualKey / VirtualKeyModifiers all round-trip through here.
        Converters = { new JsonStringEnumConverter() }
    };
}
