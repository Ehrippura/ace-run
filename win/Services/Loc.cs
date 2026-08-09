using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace ace_run.Services;

internal static class Loc
{
    private static Microsoft.Windows.ApplicationModel.Resources.ResourceLoader? _loader;
    private static Dictionary<string, string> _fallbacks;

    /// <summary>
    /// The static constructor still resolves the language on its own, so any call site that
    /// runs before <see cref="Initialize"/> gets the system language rather than nothing.
    /// </summary>
    static Loc() => _fallbacks = Resolve(null);

    /// <summary>
    /// Applies the user's language override. Must run before the first
    /// <see cref="GetString"/> call — <c>App.OnLaunched</c> does it before constructing the
    /// main window, because every string in the UI is read once, at construction, and the
    /// app has no mechanism for re-reading them. Changing the language therefore needs a
    /// restart, which the settings window says out loud.
    /// </summary>
    /// <param name="languageTag">A BCP-47 tag, or null/empty to follow the system.</param>
    public static void Initialize(string? languageTag) => _fallbacks = Resolve(languageTag);

    private static Dictionary<string, string> Resolve(string? languageTag)
    {
        if (!string.IsNullOrWhiteSpace(languageTag))
        {
            try
            {
                var culture = new CultureInfo(languageTag);
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                CultureInfo.CurrentUICulture = culture;
            }
            catch (CultureNotFoundException)
            {
                languageTag = null; // a hand-edited config.json should not brick the strings
            }
        }

        // The culture assignment above steers .NET formatting and nothing else. MRT — the
        // `ace-run.pri` next to the exe — resolves against its own ResourceContext and never
        // looks at CultureInfo, so for a long time the override changed the embedded-.resw
        // fallback while the real answers still came back in the system language.
        // PrimaryLanguageOverride is the one knob MRT reads, and it steers both halves of
        // this: the ResourceLoader below *and* XAML's x:Uid, which never comes through here.
        // It has to be set before either resolves anything, which is why App.OnLaunched
        // calls Initialize before constructing the main window.
        try
        {
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride =
                string.IsNullOrWhiteSpace(languageTag) ? string.Empty : languageTag;
        }
        catch
        {
            // An unsupported tag is refused rather than thrown at the user; the app then
            // runs in the system language, same as before the override existed.
        }

        try
        {
            _loader = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
        }
        catch
        {
            // ignore
        }

        var name = string.IsNullOrWhiteSpace(languageTag)
            ? CultureInfo.CurrentUICulture.Name
            : languageTag;

        string resourceName;
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            resourceName = "ace_run.Strings.zh-TW.Resources.resw";
        else if (name.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
            resourceName = "ace_run.Strings.ja-JP.Resources.resw";
        else
            resourceName = "ace_run.Strings.en-US.Resources.resw";

        return LoadFromEmbeddedResw(resourceName);
    }

    public static string GetString(string key)
    {
        try
        {
            var value = _loader?.GetString(key);
            if (!string.IsNullOrEmpty(value))
                return value;
        }
        catch { }

        return _fallbacks.TryGetValue(key, out var fallback) ? fallback : key;
    }

    private static Dictionary<string, string> LoadFromEmbeddedResw(string resourceName)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var stream = assembly.GetManifestResourceStream(resourceName);

            if (stream is null)
            {
                // Name mismatch fallback: search all embedded resources for a matching locale
                var locale = resourceName.Contains("zh-TW") ? "zh-TW"
                           : resourceName.Contains("ja-JP") ? "ja-JP"
                           : "en-US";
                var match = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.Contains(locale, StringComparison.Ordinal)
                                     && n.EndsWith(".resw", StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                    stream = assembly.GetManifestResourceStream(match);
            }

            if (stream is null)
                return new Dictionary<string, string>();

            var doc = XDocument.Load(stream);
            return doc.Descendants("data")
                .Where(e => e.Attribute("name") is not null)
                .ToDictionary(
                    e => e.Attribute("name")!.Value,
                    e => e.Element("value")?.Value ?? string.Empty
                );
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }
}
