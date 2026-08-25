using System.Collections.Frozen;
using System.Reflection;

namespace QuickRun.App.Daemon;

/// <summary>
/// The files the dashboard page loads: the vendored editor and the config schema.
/// <para>
/// Embedded rather than shipped beside the binary, because QuickRun is a single file - and served
/// from the daemon rather than a CDN, because a local tool has to work offline and must not tell a
/// third party which config someone is editing.
/// </para>
/// </summary>
public static class StaticAssets
{
    /// <summary>Resource names by request path, lower-cased with forward slashes.</summary>
    private static readonly FrozenDictionary<string, string> ByPath = Build();

    private static readonly FrozenDictionary<string, string> ContentTypes = new Dictionary<string, string>
    {
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".html"] = "text/html; charset=utf-8",
        [".map"] = "application/json; charset=utf-8",
        [".ttf"] = "font/ttf",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".txt"] = "text/plain; charset=utf-8",
        [".d.ts"] = "text/plain; charset=utf-8",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>The asset, or null when nothing is embedded under that path.</summary>
    public static (Stream Content, string ContentType)? Open(string path)
    {
        var key = Normalize(path);
        if (!ByPath.TryGetValue(key, out var resource)) return null;

        var stream = typeof(StaticAssets).Assembly.GetManifestResourceStream(resource);
        if (stream is null) return null;

        var extension = Path.GetExtension(key);
        return (stream, ContentTypes.GetValueOrDefault(extension, "application/octet-stream"));
    }

    private static FrozenDictionary<string, string> Build()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var name in typeof(StaticAssets).Assembly.GetManifestResourceNames())
        {
            // MSBuild writes the recursive part of a logical name with the platform's separator,
            // so both spellings have to map to the same request path.
            if (!name.StartsWith("monaco/", StringComparison.Ordinal)
                && !name.StartsWith("monaco\\", StringComparison.Ordinal)
                && name != "quickrun.schema.json")
                continue;

            map[Normalize(name)] = name;
        }

        return map.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static string Normalize(string path) =>
        path.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
}
