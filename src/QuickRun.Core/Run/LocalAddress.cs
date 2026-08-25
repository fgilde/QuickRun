using System.Text.RegularExpressions;

namespace QuickRun.Core.Run;

/// <summary>
/// Reads the address a task printed.
/// <para>
/// Most repositories never say <c>open:</c> in their config, but almost every server prints where
/// it is listening. Only loopback addresses count: a build log is full of links to documentation
/// and advisories, and none of those are where the app is running.
/// </para>
/// </summary>
public static partial class LocalAddress
{
    [GeneratedRegex(@"https?://(?:localhost|127\.0\.0\.1|0\.0\.0\.0|\[::1\])(?::\d{1,5})?(?:/[^\s""'<>,;]*)?",
        RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();

    /// <summary>The first address in a line of output.</summary>
    public static string? In(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var match = Pattern().Match(text);
        return match.Success ? Clean(match.Value) : null;
    }

    /// <summary>
    /// The last address in a whole log. A server that rebound, or printed a summary after its
    /// startup noise, means the newest line is the one still true.
    /// </summary>
    public static string? Last(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        string? found = null;
        foreach (Match match in Pattern().Matches(text)) found = Clean(match.Value);
        return found;
    }

    // 0.0.0.0 means "every interface", which is not an address a browser can open.
    private static string Clean(string value) =>
        value.Replace("0.0.0.0", "localhost", StringComparison.Ordinal)
            .TrimEnd('.', ',', ')', ']', '"', '\'');
}
