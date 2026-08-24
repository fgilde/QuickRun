using System.Globalization;
using System.Text.RegularExpressions;
using QuickRun.Core.Config;

namespace QuickRun.Core.Inputs;

public sealed record InputError(string Id, string Message);

/// <summary>
/// Defaults, validates and maps the values behind a config's <c>inputs</c>. Validation collects
/// rather than throws, so a form can show every problem at once.
/// </summary>
public static class InputResolver
{
    public static IReadOnlyDictionary<string, string?> ApplyDefaults(
        IReadOnlyList<InputDef> defs, IReadOnlyDictionary<string, string?> provided)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var def in defs)
            result[def.Id] = provided.TryGetValue(def.Id, out var value) && value is not null ? value : def.Default;
        return result;
    }

    public static IReadOnlyList<InputError> Validate(
        IReadOnlyList<InputDef> defs, IReadOnlyDictionary<string, string?> values)
    {
        var errors = new List<InputError>();

        foreach (var def in defs)
        {
            var raw = values.GetValueOrDefault(def.Id);
            var empty = string.IsNullOrWhiteSpace(raw);

            if (def.Required && empty)
            {
                errors.Add(new(def.Id, $"'{def.Id}' is required"));
                continue;
            }
            if (empty) continue;

            switch (def.Type)
            {
                case InputType.Number:
                    if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                        errors.Add(new(def.Id, $"'{def.Id}' must be a number"));
                    else if (def.Min is { } min && number < min)
                        errors.Add(new(def.Id, $"'{def.Id}' must be at least {min}"));
                    else if (def.Max is { } max && number > max)
                        errors.Add(new(def.Id, $"'{def.Id}' must be at most {max}"));
                    break;

                case InputType.Bool:
                    if (!bool.TryParse(raw, out _))
                        errors.Add(new(def.Id, $"'{def.Id}' must be true or false"));
                    break;

                case InputType.Select:
                    if (!def.Options.Any(o => string.Equals(o.Value, raw, StringComparison.Ordinal)))
                        errors.Add(new(def.Id,
                            $"'{def.Id}' must be one of {string.Join(", ", def.Options.Select(o => o.Value))}"));
                    break;

                default:
                    // Text, Password, Path, Dir and File all honour the pattern. Existence checks for
                    // Dir and File belong to the run, not here: the workspace does not exist yet.
                    if (!string.IsNullOrWhiteSpace(def.Pattern) && !Regex.IsMatch(raw!, def.Pattern))
                        errors.Add(new(def.Id, $"'{def.Id}' does not match {def.Pattern}"));
                    break;
            }
        }

        return errors;
    }

    public static IReadOnlyDictionary<string, string> ToEnv(
        IReadOnlyList<InputDef> defs, IReadOnlyDictionary<string, string?> values) =>
        defs.Where(d => !string.IsNullOrWhiteSpace(d.Env))
            .Select(d => (Name: d.Env!, Value: values.GetValueOrDefault(d.Id)))
            .Where(x => x.Value is not null)
            .ToDictionary(x => x.Name, x => x.Value!, StringComparer.Ordinal);

    public static IReadOnlyList<string> SecretIds(IReadOnlyList<InputDef> defs) =>
        defs.Where(d => d.Type == InputType.Password).Select(d => d.Id).ToList();

    /// <summary>Turns the CLI's <c>--input key=value</c> occurrences into a dictionary.</summary>
    public static IReadOnlyDictionary<string, string?> ParseAssignments(IEnumerable<string> assignments)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var assignment in assignments)
        {
            var separator = assignment.IndexOf('=');
            if (separator <= 0) throw new ArgumentException($"expected key=value, got '{assignment}'");
            result[assignment[..separator]] = assignment[(separator + 1)..];
        }
        return result;
    }
}
