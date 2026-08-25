using System.Globalization;
using System.Text;

namespace QuickRun.Core.Config;

/// <summary>
/// Writes a <see cref="RunConfig"/> back out as YAML.
/// <para>
/// Needed wherever a config was derived rather than written: a Pinokio app's scripts, a detected
/// entry point. Handing the user the YAML is what turns "QuickRun guessed this" into a file they can
/// edit, test and commit - and what the config builder starts from.
/// </para>
/// <para>
/// Hand-written rather than a serialiser: the shape has to match what a person would have typed,
/// with the shorthand forms and without a wall of nulls.
/// </para>
/// </summary>
public static class ConfigWriter
{
    public const string SchemaLine =
        "# yaml-language-server: $schema=https://fgilde.github.io/QuickRun/quickrun.schema.json";

    public static string ToYaml(RunConfig config, string? header = null)
    {
        var output = new StringBuilder();
        output.AppendLine(SchemaLine);
        if (!string.IsNullOrWhiteSpace(header)) output.AppendLine($"# {header}");
        output.AppendLine($"version: {config.Version}");

        if (config.Name is { } name) output.AppendLine($"name: {Scalar(name)}");
        if (config.Description is { } description) output.AppendLine($"description: {Scalar(description)}");
        if (config.Icon is { } icon) output.AppendLine($"icon: {Scalar(icon)}");
        if (config.Docs is { } docs) output.AppendLine($"docs: {Scalar(docs)}");

        Requires(output, config.Requires);
        Inputs(output, config.Inputs);
        Env(output, "env", config.Env, indent: "  ");
        Steps(output, "setup", config.Setup);
        Tasks(output, config.Tasks);
        Steps(output, "stop", config.Stop);

        return output.ToString();
    }

    private static void Requires(StringBuilder output, IReadOnlyList<ToolRequirement> requirements)
    {
        if (requirements.Count == 0) return;

        output.AppendLine();
        output.AppendLine("requires:");

        foreach (var requirement in requirements)
        {
            output.AppendLine($"  - tool: {Scalar(requirement.Tool)}");
            if (requirement.Version is { } version) output.AppendLine($"    version: {Scalar(version)}");
            if (requirement.Install is { } install) output.AppendLine($"    install: {Scalar(install)}");
            if (requirement.Optional) output.AppendLine("    optional: true");
        }
    }

    private static void Inputs(StringBuilder output, IReadOnlyList<InputDef> inputs)
    {
        if (inputs.Count == 0) return;

        output.AppendLine();
        output.AppendLine("inputs:");

        foreach (var input in inputs)
        {
            output.AppendLine($"  - id: {Scalar(input.Id)}");
            if (input.Label is { } label) output.AppendLine($"    label: {Scalar(label)}");
            output.AppendLine($"    type: {input.Type.ToString().ToLowerInvariant()}");
            if (input.Description is { } description) output.AppendLine($"    description: {Scalar(description)}");
            if (input.Default is { } fallback) output.AppendLine($"    default: {Scalar(fallback)}");
            if (input.Required) output.AppendLine("    required: true");
            if (input.Pattern is { } pattern) output.AppendLine($"    pattern: {Scalar(pattern)}");
            if (input.Min is { } min) output.AppendLine($"    min: {Number(min)}");
            if (input.Max is { } max) output.AppendLine($"    max: {Number(max)}");
            if (input.Env is { } env) output.AppendLine($"    env: {Scalar(env)}");
            if (input.Persist) output.AppendLine("    persist: true");

            if (input.Options.Count == 0) continue;

            output.AppendLine("    options:");
            foreach (var option in input.Options)
                output.AppendLine(option.Label is null
                    ? $"      - {Scalar(option.Value)}"
                    : $"      - {{value: {Scalar(option.Value)}, label: {Scalar(option.Label)}}}");
        }
    }

    private static void Steps(StringBuilder output, string section, IReadOnlyList<Step> steps)
    {
        if (steps.Count == 0) return;

        output.AppendLine();
        output.AppendLine($"{section}:");

        foreach (var step in steps)
        {
            output.AppendLine($"  - run: {Scalar(step.Run)}");
            if (step.Cwd is { } cwd) output.AppendLine($"    cwd: {Scalar(cwd)}");
            if (step.When.Count > 0) output.AppendLine($"    when: [{string.Join(", ", step.When)}]");
            if (step.ContinueOnError) output.AppendLine("    continueOnError: true");
        }
    }

    private static void Tasks(StringBuilder output, IReadOnlyList<TaskDef> tasks)
    {
        if (tasks.Count == 0) return;

        output.AppendLine();
        output.AppendLine("tasks:");

        foreach (var task in tasks)
        {
            output.AppendLine($"  - name: {Scalar(task.Name)}");
            output.AppendLine($"    run: {Scalar(task.Run)}");
            if (task.Cwd is { } cwd) output.AppendLine($"    cwd: {Scalar(cwd)}");
            Env(output, "env", task.Env, indent: "    ");
            if (task.DependsOn.Count > 0)
                output.AppendLine($"    dependsOn: [{string.Join(", ", task.DependsOn.Select(Scalar))}]");

            if (task.ReadyWhen is { } ready) output.AppendLine($"    readyWhen: {Ready(ready)}");

            if (task.OpenUrl is { } url) output.AppendLine($"    open: {Scalar(url)}");
            else if (task.OpenReady) output.AppendLine("    open: true");

            if (task.Restart == RestartPolicy.OnFailure) output.AppendLine("    restart: onFailure");
        }
    }

    private static string Ready(ReadyWhen ready) => ready switch
    {
        { Port: { } port } => $"{{port: {port}}}",
        { Http: { } http } => $"{{http: {Scalar(http)}}}",
        { Log: { } log } => $"{{log: {Single(log)}}}",
        { Window: true } => "{window: true}",
        { Delay: { } delay } => $"{{delay: {(int)delay.TotalMilliseconds}ms}}",
        _ => "{}",
    };

    private static void Env(StringBuilder output, string section,
        IReadOnlyDictionary<string, string> values, string indent)
    {
        if (values.Count == 0) return;

        var head = indent.Length > 2 ? indent : "";
        output.AppendLine($"{head}{section}:");
        foreach (var (key, value) in values)
            output.AppendLine($"{indent}  {key}: {Scalar(value)}");
    }

    private static string Number(double value) =>
        value == Math.Floor(value)
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// A regular expression goes in single quotes: in a double-quoted scalar <c>\S</c> is an invalid
    /// escape and the file would not parse - the same trap the documentation warns about.
    /// </summary>
    private static string Single(string value) => $"'{value.Replace("'", "''")}'";

    private static string Scalar(string value)
    {
        if (value.Length == 0) return "\"\"";

        var risky = value.AsSpan().IndexOfAny(":#{}[],&*!|>%@\"'`\n\r\t") >= 0
                    || value.StartsWith('-')
                    || value.Trim() != value
                    || bool.TryParse(value, out _)
                    || double.TryParse(value, CultureInfo.InvariantCulture, out _);

        if (!risky) return value;

        return '"' + value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t") + '"';
    }
}
