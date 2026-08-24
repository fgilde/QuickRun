using System.Text.RegularExpressions;

namespace QuickRun.Core.Requires;

/// <summary>
/// Compares tool versions against simple ranges (">=9.0", ">20", "&lt;=3.12", "=1.2.3", "1.2.3").
/// Deliberately not a semver implementation: tool output is not semver, and ranges in
/// quickrun.yml are single comparisons.
/// </summary>
public static partial class VersionCheck
{
    private static readonly string[] Operators = { ">=", "<=", ">", "<", "=" };

    public static bool Satisfies(string? found, string? range)
    {
        if (string.IsNullOrWhiteSpace(range)) return true;
        if (string.IsNullOrWhiteSpace(found)) return false;

        var (op, wanted) = SplitRange(range.Trim());
        var actual = Parse(found);
        var expected = Parse(wanted);
        if (actual is null || expected is null) return false;

        var length = op == "=" ? expected.Length : Math.Max(actual.Length, expected.Length);
        var cmp = Compare(actual, expected, length);

        return op switch
        {
            ">=" => cmp >= 0,
            ">" => cmp > 0,
            "<=" => cmp <= 0,
            "<" => cmp < 0,
            _ => cmp == 0,
        };
    }

    /// <summary>Pulls the first dotted version out of arbitrary tool output.</summary>
    public static string? Extract(string text)
    {
        var match = VersionPattern().Match(text ?? "");
        return match.Success ? match.Value : null;
    }

    private static (string Operator, string Version) SplitRange(string range)
    {
        foreach (var op in Operators)
            if (range.StartsWith(op, StringComparison.Ordinal))
                return (op, range[op.Length..].Trim());
        return ("=", range);
    }

    private static int[]? Parse(string text)
    {
        var version = Extract(text);
        return version?.Split('.').Select(part => int.TryParse(part, out var n) ? n : 0).ToArray();
    }

    private static int Compare(int[] actual, int[] expected, int length)
    {
        for (var i = 0; i < length; i++)
        {
            var a = i < actual.Length ? actual[i] : 0;
            var b = i < expected.Length ? expected[i] : 0;
            if (a != b) return a.CompareTo(b);
        }
        return 0;
    }

    [GeneratedRegex(@"\d+(\.\d+)*")]
    private static partial Regex VersionPattern();
}
