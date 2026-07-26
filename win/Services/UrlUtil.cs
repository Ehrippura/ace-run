using System;

namespace ace_run.Services;

/// <summary>
/// URL parsing for <see cref="Models.ItemKind.Url"/> items. Any absolute URI whose
/// scheme is not <c>file</c> counts — so <c>steam://</c>, <c>mailto:</c> and
/// <c>ms-settings:</c> work alongside http(s). <c>file:</c> is rejected because a
/// local path belongs to an App item instead.
/// </summary>
internal static class UrlUtil
{
    /// <summary>
    /// Trims <paramref name="input"/> and, when it looks like a bare host, prefixes
    /// <c>https://</c> so the user can type just "github.com".
    /// </summary>
    /// <returns>false when the result is not a launchable URL.</returns>
    public static bool TryNormalize(string? input, out string url)
    {
        url = string.Empty;

        var text = input?.Trim();
        if (string.IsNullOrEmpty(text))
            return false;

        // Host-shaped with no scheme ("example.com", "example.com:8080/path") — assume https.
        if (!HasScheme(text) && text.Contains('.') && !text.Contains(' '))
            text = "https://" + text;

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.IsFile)
            return false;

        url = text;
        return true;
    }

    /// <summary>
    /// True when the text starts with a URI scheme. Deliberately rejects a dotted prefix
    /// so "example.com:8080" is read as host:port rather than as a scheme named "example.com"
    /// (which <see cref="Uri"/> would otherwise happily accept).
    /// </summary>
    private static bool HasScheme(string text)
    {
        var colon = text.IndexOf(':');
        if (colon <= 0)
            return false;

        var scheme = text.AsSpan(0, colon);
        if (!char.IsAsciiLetter(scheme[0]))
            return false;

        foreach (var c in scheme)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '+' && c != '-')
                return false;
        }

        return true;
    }

    /// <summary>
    /// Default display name for a URL: the host without a leading "www.".
    /// Falls back to the raw string for schemes without a host (mailto:, ms-settings:).
    /// </summary>
    public static string SuggestDisplayName(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;

        var host = uri.Host;
        if (string.IsNullOrEmpty(host))
            return url;

        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? host.Substring(4)
            : host;
    }

    /// <summary>
    /// Reads the <c>URL=</c> entry out of a .url Internet Shortcut file (an INI with an
    /// <c>[InternetShortcut]</c> section). Returns null when the file can't be read or has no URL.
    /// </summary>
    public static string? ReadInternetShortcut(string path)
    {
        try
        {
            foreach (var line in System.IO.File.ReadLines(path))
            {
                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                    return line.Substring(4).Trim();
            }
        }
        catch
        {
            // Unreadable shortcut — treat as "no URL".
        }

        return null;
    }
}
