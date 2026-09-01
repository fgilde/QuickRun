using System.Text.RegularExpressions;
using QuickRun.Core.Config;

namespace QuickRun.Core.Detect;

/// <summary>
/// A variable a compose file needs and cannot supply itself.
/// </summary>
/// <param name="Default">
/// What to put in the field. From the repository's own example file, never invented: a made-up
/// password looks like a working default and is one nobody chose.
/// </param>
/// <param name="Secret">Whether the value is hidden while typing and redacted out of the log.</param>
public sealed record ComposeVariable(string Name, string? Default, bool Secret);

/// <summary>
/// The questions a docker-compose file is really asking.
/// <para>
/// A compose file that writes <c>${POSTGRES_PASSWORD}</c> with no default gets an empty string when
/// nothing supplies one, and an empty string is how a postgres refuses to start and an application
/// fails against it. The repository usually ships the answer in a <c>.env.example</c> it also
/// gitignores as <c>.env</c> - present for a person to copy, absent for a machine that clones.
/// </para>
/// <para>
/// So the detector asks. Compose reads variables from the environment it is started in, and an input
/// with <c>env:</c> is put there, which is why this needs nothing else to work.
/// </para>
/// </summary>
public static partial class ComposeVariables
{
    /// <summary>
    /// Beyond this many fields a form stops being a form. Reached only by a compose file that asks
    /// for more than a person would fill in by hand anyway, and the run can still be edited.
    /// </summary>
    public const int Most = 20;

    /// <summary>Names whose value is hidden while typing and redacted out of every log line.</summary>
    private static readonly string[] SecretWords =
        { "PASSWORD", "SECRET", "TOKEN", "APIKEY", "API_KEY", "PRIVATE", "CREDENTIAL", "PASSWD", "SALT" };

    /// <summary>
    /// <c>${VAR}</c>, <c>${VAR:-default}</c>, <c>${VAR-default}</c>, <c>${VAR:?message}</c>.
    /// <para>
    /// The leading <c>(^|[^$])</c> is what keeps <c>$${VAR}</c> out: in a compose file that is an
    /// escaped dollar meant for the container's own shell, and asking the user for it would be
    /// asking about something that never reaches compose at all.
    /// </para>
    /// </summary>
    [GeneratedRegex(@"(^|[^$])\$\{([A-Za-z_][A-Za-z0-9_]*)(?<op>:?[-?])?(?<rest>[^}]*)\}",
        RegexOptions.Multiline)]
    private static partial Regex Placeholder();

    /// <summary>A <c>KEY=value</c> line, as an env file writes it.</summary>
    [GeneratedRegex(@"^\s*(?:export\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$")]
    private static partial Regex Assignment();

    /// <summary>The env files a repository ships for copying, in the order they are believed.</summary>
    public static readonly string[] ExampleNames =
        { ".env.example", ".env.sample", ".env.template", ".env.dist", "env.example" };

    /// <summary>
    /// What has to be asked before this compose file can run.
    /// <para>
    /// Only what compose cannot answer on its own: a placeholder that carries its own default needs
    /// nobody, and one whose name is already set in a real <c>.env</c> beside the file is answered
    /// too. What is left would arrive empty.
    /// </para>
    /// </summary>
    /// <param name="compose">The compose file's text.</param>
    /// <param name="example">An example env file's text, when the repository ships one.</param>
    /// <param name="present">Variables an actual .env beside the compose file already sets.</param>
    public static IReadOnlyList<ComposeVariable> In(
        string? compose, string? example = null, IReadOnlySet<string>? present = null)
    {
        if (string.IsNullOrWhiteSpace(compose)) return Array.Empty<ComposeVariable>();

        var defaults = Values(example);
        var asked = new List<ComposeVariable>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var match in Placeholder().Matches(compose).Cast<Match>())
        {
            var name = match.Groups[2].Value;
            var op = match.Groups["op"].Value;

            // ":-" and "-" carry a default, so compose fills this one in itself. ":?" and "?" carry
            // a message rather than a value: compose refuses to start without it, which is exactly
            // the variable worth asking about.
            if (op is ":-" or "-") continue;

            if (present is not null && present.Contains(name)) continue;
            if (!seen.Add(name)) continue;

            asked.Add(new ComposeVariable(name, defaults.GetValueOrDefault(name), IsSecret(name)));
        }

        // Whatever a person has to invent comes before what the repository already suggests: the
        // empty fields are the ones the run actually waits on.
        return asked
            .OrderBy(v => v.Default is { Length: > 0 } ? 1 : 0)
            .ThenBy(v => v.Name, StringComparer.Ordinal)
            .Take(Most)
            .ToList();
    }

    /// <summary>The same, reading the files beside a compose file in a directory.</summary>
    public static IReadOnlyList<ComposeVariable> Beside(string dir, string composeFile)
    {
        var example = ExampleNames
            .Select(n => Path.Combine(dir, n))
            .FirstOrDefault(File.Exists);

        var real = Path.Combine(dir, ".env");

        return In(
            Text(composeFile),
            example is null ? null : Text(example),
            File.Exists(real) ? Values(Text(real)).Keys.ToHashSet(StringComparer.Ordinal) : null);
    }

    /// <summary>An env file's assignments. Quotes are stripped; a quoted empty value stays empty.</summary>
    public static IReadOnlyDictionary<string, string> Values(string? text)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(text)) return values;

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#') continue;

            var match = Assignment().Match(trimmed);
            if (!match.Success) continue;

            var value = match.Groups[2].Value.Trim();

            // An unquoted trailing comment is not part of the value. A quoted one is.
            if (value.Length > 0 && value[0] is not ('"' or '\'') && value.Contains(" #"))
                value = value[..value.IndexOf(" #", StringComparison.Ordinal)].TrimEnd();

            if (value.Length >= 2 && (value[0] is '"' or '\'') && value[^1] == value[0])
                value = value[1..^1];

            values[match.Groups[1].Value] = value;
        }

        return values;
    }

    /// <summary>The inputs these variables become, as they go into a generated config.</summary>
    public static IReadOnlyList<InputDef> ToInputs(IReadOnlyList<ComposeVariable> variables) =>
        variables.Select(v => new InputDef(
            Id: v.Name,
            Label: v.Name,
            Type: v.Secret ? InputType.Password : InputType.Text,
            Description: null,
            Default: v.Default,
            // Required only where nothing is suggested: with a default in the field, empty is a
            // choice somebody made, and the compose file is the one that decides what to do with it.
            Required: string.IsNullOrEmpty(v.Default),
            Pattern: null,
            Min: null,
            Max: null,
            Options: Array.Empty<InputOption>(),
            Env: v.Name,
            Persist: false)).ToList();

    private static bool IsSecret(string name) =>
        SecretWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase));

    private static string? Text(string path)
    {
        try { return File.ReadAllText(path); } catch (IOException) { return null; } catch (UnauthorizedAccessException) { return null; }
    }
}
