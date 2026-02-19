using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace ace_run.Services;

internal static class Loc
{
    private static readonly Microsoft.Windows.ApplicationModel.Resources.ResourceLoader? _loader;
    private static readonly Dictionary<string, string> _fallbacks;

    static Loc()
    {
        try
        {
            _loader = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
        }
        catch
        {
            // ignore
        }

        var isZh = CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        var resourceName = isZh
            ? "ace_run.Strings.zh-TW.Resources.resw"
            : "ace_run.Strings.en-US.Resources.resw";
        _fallbacks = LoadFromEmbeddedResw(resourceName);
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
                var locale = resourceName.Contains("zh-TW") ? "zh-TW" : "en-US";
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
