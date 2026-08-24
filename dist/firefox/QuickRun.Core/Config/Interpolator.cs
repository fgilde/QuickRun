using System.Text.RegularExpressions;

namespace QuickRun.Core.Config;

public sealed record InterpolationContext(
    IReadOnlyDictionary<string, string?> Inputs,
    string Workspace,
    string RepoName,
    string RepoRef,
    Func<string, string?>? EnvLookup = null);

public sealed class InterpolationException(string message) : Exception(message);

/// <summary>
/// Expands <c>${inputs.x}</c>, <c>${env.X}</c>, <c>${workspace}</c>, <c>${repo.name}</c> and
/// <c>${repo.ref}</c>, and redacts secret values out of anything shown to the user.
/// </summary>
public static partial class Interpolator
{
    /// <summary>Below this length a secret is not redacted - blanket-replacing "x" mangles every log line.</summary>
    private const int MinRedactableLength = 4;

    public static string Expand(string template, InterpolationContext ctx) =>
        Placeholder().Replace(template, m => Resolve(m.Groups[1].Value, ctx));

    /// <summary>The distinct, long-enough values behind the given input ids.</summary>
    public static IReadOnlyList<string> Secrets(
        IReadOnlyDictionary<string, string?> values, IEnumerable<string> secretIds) =>
        secretIds
            .Select(values.GetValueOrDefault)
            .Where(v => !string.IsNullOrEmpty(v) && v.Length >= MinRedactableLength)
            .Select(v => v!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    public static string Redact(string text, IReadOnlyList<string> secrets)
    {
        foreach (var secret in secrets) text = text.Replace(secret, "***", StringComparison.Ordinal);
        return text;
    }

    /// <summary>Every placeholder in the text, as written (without the <c>${}</c>).</summary>
    public static IEnumerable<string> Placeholders(string template) =>
        Placeholder().Matches(template).Select(m => m.Groups[1].Value);

    private static string Resolve(string expression, InterpolationContext ctx)
    {
        switch (expression)
        {
            case "workspace": return ctx.Workspace;
            case "repo.name": return ctx.RepoName;
            case "repo.ref": return ctx.RepoRef;
        }

        var parts = expression.Split('.', 2);
        if (parts.Length != 2) throw new InterpolationException($"unknown placeholder '${{{expression}}}'");

        return parts[0] switch
        {
            "inputs" => ctx.Inputs.TryGetValue(parts[1], out var value)
                ? value ?? ""
                : throw new InterpolationException($"unknown input reference '{parts[1]}'"),
            "env" => (ctx.EnvLookup ?? Environment.GetEnvironmentVariable)(parts[1]) ?? "",
            _ => throw new InterpolationException($"unknown placeholder namespace '{parts[0]}'"),
        };
    }

    [GeneratedRegex(@"\$\{([A-Za-z_][A-Za-z0-9_.]*)\}")]
    private static partial Regex Placeholder();
}
