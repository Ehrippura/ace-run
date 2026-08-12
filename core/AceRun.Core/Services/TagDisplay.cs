using System.Collections.Generic;
using System.Linq;

namespace ace_run.Services;

/// <summary>
/// How many tags get shown before the rest fold into a counter.
/// </summary>
/// <remarks>
/// Two surfaces show the same truncated strip — the dots on a tile and the dots on the edit
/// dialog's tag button — and they had a <c>= 3</c> constant each, plus a copy of the arithmetic.
/// The count is a layout decision (three dots is what fits beside a tile's name), so it belongs
/// in one place even though the drawing does not.
/// </remarks>
public static class TagDisplay
{
    /// <summary>Tags shown individually; beyond this they are counted instead.</summary>
    public const int MaxVisible = 3;

    /// <summary>
    /// The leading tags that get drawn.
    /// </summary>
    /// <remarks>
    /// Returns <paramref name="tags"/> itself when nothing is truncated, rather than a copy.
    /// This is read once per bound item, so the common case — an item with three tags or
    /// fewer — must not allocate on every read while the grid scrolls.
    /// </remarks>
    public static IReadOnlyList<T> Visible<T>(IReadOnlyList<T> tags, int max = MaxVisible)
        => tags.Count <= max ? tags : tags.Take(max).ToList();

    /// <summary>How many tags <see cref="Visible{T}"/> left out. Zero means no counter is shown.</summary>
    public static int OverflowCount<T>(IReadOnlyList<T> tags, int max = MaxVisible)
        => tags.Count > max ? tags.Count - max : 0;
}
