using System.Text.Json.Serialization;

namespace ace_run.Models;

/// <summary>
/// The main window's persisted size, in DIPs.
/// </summary>
/// <remarks>
/// DIPs, and the property names say so, because the previous shape is exactly what went wrong:
/// builds before this stored <c>AppWindow.Size</c> raw — physical pixels — under plain
/// <c>Width</c>/<c>Height</c>, and nothing in the file recorded the scale they were captured at.
/// A size saved on a 100% display therefore restored half again as large on a 150% one.
///
/// The unit change rides on the key names rather than <see cref="WorkspaceConfig.Version"/>.
/// <c>DataStore.SaveConfig</c> stamps the current version on every write, and startup repair
/// (<c>MigrateOrInitialize</c> → <c>EnsureUsable</c>) can reach it before the window has ever
/// been resized — a version-gated migration would then label a pixel value as a DIP one and the
/// window would come back oversized for good. Key presence cannot be got wrong that way, and it
/// keeps the conversion in the layer that has a scale to convert with: the data layer has no
/// display to ask, and <c>LoadConfig</c>'s other callers have no business knowing about DPI.
/// </remarks>
public class WindowState
{
    public int WidthDip { get; set; }
    public int HeightDip { get; set; }

    /// <summary>
    /// Physical pixels, written by builds before the DIP switch. Migration input only:
    /// <see cref="Services.WindowPlacement.ResolveStartupSize"/> converts it once with the
    /// current scale, and the next save writes the DIP pair alone. Omitted from new files, so
    /// its presence is what marks a file as pre-DIP.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Width { get; set; }

    /// <inheritdoc cref="Width"/>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Height { get; set; }
}
