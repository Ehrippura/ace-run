using System.Collections.Generic;

namespace ace_run.Services;

/// <summary>
/// The colour keys a workspace or a tag can carry.
/// </summary>
/// <remarks>
/// <para>
/// <b>These strings are persisted to JSON and must never be renamed.</b> A workspace's
/// <c>ColorTag</c> and a tag's <c>ColorKey</c> are stored verbatim, and the dialogs carry the
/// key in a combo item's <c>Tag</c> while showing something else as its text — so the display
/// wording is free to change and the key is not. Renaming one silently drops the colour from
/// every existing item that used it.
/// </para>
/// <para>
/// They live apart from the brushes they resolve to because that resolution needs a running
/// XAML application. Keeping the list in the same class as a <c>SolidColorBrush</c> static
/// field meant reading these six strings ran the type initializer and threw without a UI.
/// </para>
/// </remarks>
public static class ColorKeys
{
    /// <summary>Selectable colour keys, in display order.</summary>
    public static IReadOnlyList<string> All { get; } =
        ["Blue", "Green", "Red", "Yellow", "Purple", "Gray"];

    /// <summary>What a tag gets when nothing was chosen.</summary>
    public const string Default = "Blue";
}
