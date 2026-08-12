using System;
using System.IO;

namespace ace_run.Services;

/// <summary>
/// Which folder a file / folder picker should open at, derived from what the field it feeds
/// already holds.
/// </summary>
/// <remarks>
/// The picker's own "remember where you were last time" is the wrong answer while editing an
/// existing item: the user is looking for something next to the value already in the box —
/// the working directory being changed, the icon being replaced — not next to whatever they
/// happened to browse for in some earlier dialog.
/// </remarks>
public static class PickerStart
{
    /// <summary>
    /// The folder holding <paramref name="filePath"/>, or null when there is none to name: an
    /// empty box, a URL, or a bare filename with no directory part.
    /// </summary>
    public static string? DirectoryOf(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return null;

        try
        {
            var dir = Path.GetDirectoryName(filePath.Trim());
            return string.IsNullOrEmpty(dir) ? null : dir;
        }
        catch (ArgumentException)
        {
            // Whatever is in the box is not a path at all. No start folder beats throwing out
            // of a click handler.
            return null;
        }
    }

    /// <summary>
    /// The first candidate that names a folder on disk, or null to leave the choice to the
    /// picker.
    /// </summary>
    /// <param name="directoryExists">
    /// Existence test, injected so the preference order can be exercised without a filesystem.
    /// </param>
    /// <remarks>
    /// Relative paths are skipped rather than resolved. <see cref="Directory.Exists(string)"/>
    /// would resolve one against the process's current directory — wherever the app happened
    /// to be launched from, which has nothing to do with the item being edited — and quietly
    /// open the picker somewhere the user never named.
    /// </remarks>
    public static string? FirstExisting(Func<string, bool> directoryExists, params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;

            var path = candidate.Trim();
            if (!IsFullyQualified(path)) continue;
            if (directoryExists(path)) return path;
        }

        return null;
    }

    private static bool IsFullyQualified(string path)
    {
        try
        {
            return Path.IsPathFullyQualified(path);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
