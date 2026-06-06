using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ace_run.Services;

/// <summary>
/// Shared color palette for workspace color tags and app tags.
/// Keys are stable strings persisted to JSON; brushes map to fixed Fluent colors.
/// </summary>
internal static class ColorTags
{
    /// <summary>Selectable color keys, in display order.</summary>
    public static readonly IReadOnlyList<string> Keys = new[]
    {
        "Blue", "Green", "Red", "Yellow", "Purple", "Gray"
    };

    public static Brush GetBrush(string? colorKey) => colorKey switch
    {
        "Blue"   => new SolidColorBrush(Color.FromArgb(255, 0, 120, 212)),
        "Green"  => new SolidColorBrush(Color.FromArgb(255, 16, 124, 16)),
        "Red"    => new SolidColorBrush(Color.FromArgb(255, 196, 43, 28)),
        "Yellow" => new SolidColorBrush(Color.FromArgb(255, 247, 99, 12)),
        "Purple" => new SolidColorBrush(Color.FromArgb(255, 136, 23, 152)),
        "Gray"   => new SolidColorBrush(Color.FromArgb(255, 118, 118, 118)),
        _        => new SolidColorBrush(Colors.Transparent)
    };
}
