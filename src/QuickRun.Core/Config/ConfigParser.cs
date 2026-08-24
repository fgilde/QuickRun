using System.Globalization;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace QuickRun.Core.Config;

/// <summary>
/// Reads quickrun.yml and expands every shorthand into the canonical model, so that the runner
/// only ever sees one shape. Syntax problems throw; incoherent content is the validator's job.
/// </summary>
public static partial class ConfigParser
{
    public static readonly string[] FileNames = { "quickrun.yml", "quickrun.yaml" };

    private static readonly string[] PlatformKeys = { "windows", "linux", "macos" };

    internal static readonly string[] TopLevelKeys =
        { "version", "name", "description", "icon", "docs", "requires", "inputs", "env", "setup", "run", "tasks", "stop" };

    internal static readonly string[] StepKeys = { "run", "cwd", "when", "continueOnError" };

    internal static readonly string[] TaskKeys =
        { "name", "run", "cwd", "env", "dependsOn", "readyWhen", "open", "restart" };

    internal static readonly string[] RequireKeys = { "tool", "version", "install", "optional" };

    internal static readonly string[] InputKeys =
        { "id", "label", "type", "description", "default", "required", "pattern", "min", "max", "options", "env", "persist" };

    internal static readonly string[] ReadyWhenKeys = { "port", "http", "log", "delay" };

    public static RunConfig Parse(string yaml, OSKind os)
    {
        Dictionary<string, object?> root;
        try
        {
            var raw = new DeserializerBuilder().Build().Deserialize<object?>(yaml);
            if (raw is null) return RunConfigDefaults.Empty;
            root = AsMap(raw) ?? throw new ConfigException("config must be a mapping of keys");
        }
        catch (YamlException e)
        {
            throw new ConfigException($"invalid YAML at line {e.Start.Line}: {e.Message}");
        }

        RejectUnknown(root, TopLevelKeys, "");

        return new RunConfig(
            Version: ParseInt(root.GetValueOrDefault("version"), "version") ?? 1,
            Name: Str(root.GetValueOrDefault("name")),
            Description: Str(root.GetValueOrDefault("description")),
            Icon: Str(root.GetValueOrDefault("icon")),
            Docs: Str(root.GetValueOrDefault("docs")),
            Requires: ParseRequires(root.GetValueOrDefault("requires")),
            Inputs: ParseInputs(root.GetValueOrDefault("inputs")),
            Env: ParseStringMap(root.GetValueOrDefault("env"), "env"),
            Setup: ParseSteps(root.GetValueOrDefault("setup"), os, "setup"),
            Tasks: ParseTasks(root, os),
            Stop: ParseSteps(root.GetValueOrDefault("stop"), os, "stop"));
    }

    public static string? FindConfigFile(string repoDir) =>
        FileNames.Select(n => Path.Combine(repoDir, n)).FirstOrDefault(File.Exists);

    /// <summary>A root run script, used when the repository has no config at all.</summary>
    public static string? FindRootScript(string repoDir, OSKind os)
    {
        var order = os == OSKind.Windows
            ? new[] { "quickrun.ps1", "quickrun.sh", "run.ps1", "run.sh" }
            : new[] { "quickrun.sh", "run.sh" };
        return order.Select(n => Path.Combine(repoDir, n)).FirstOrDefault(File.Exists);
    }

    // ---- structural helpers -------------------------------------------------

    private static Dictionary<string, object?>? AsMap(object? node)
    {
        if (node is not IDictionary<object, object?> map) return null;
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var kv in map) result[kv.Key?.ToString() ?? ""] = kv.Value;
        return result;
    }

    private static List<object?>? AsList(object? node) =>
        node is IList<object?> list ? list.ToList() : null;

    private static void RejectUnknown(Dictionary<string, object?> map, string[] allowed, string path)
    {
        foreach (var key in map.Keys)
            if (!allowed.Contains(key, StringComparer.Ordinal))
                throw new ConfigException(
                    $"{Prefix(path)}unknown key '{key}' - expected one of {string.Join(", ", allowed)}");
    }

    private static string Prefix(string path) => path.Length == 0 ? "" : path + ": ";

    private static string? Str(object? node) => node?.ToString();

    private static int? ParseInt(object? node, string path)
    {
        if (node is null) return null;
        if (int.TryParse(Str(node), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return n;
        throw new ConfigException($"{path}: expected a whole number, got '{Str(node)}'");
    }

    private static double? ParseDouble(object? node, string path)
    {
        if (node is null) return null;
        if (double.TryParse(Str(node), NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return n;
        throw new ConfigException($"{path}: expected a number, got '{Str(node)}'");
    }

    private static bool ParseBool(object? node, string path, bool fallback = false)
    {
        if (node is null) return fallback;
        if (bool.TryParse(Str(node), out var b)) return b;
        throw new ConfigException($"{path}: expected true or false, got '{Str(node)}'");
    }

    private static IReadOnlyList<string> StringList(object? node)
    {
        if (node is null) return Array.Empty<string>();
        if (AsList(node) is { } list) return list.Select(x => Str(x) ?? "").ToList();
        return new[] { Str(node) ?? "" };
    }

    private static IReadOnlyDictionary<string, string> ParseStringMap(object? node, string path)
    {
        if (node is null) return new Dictionary<string, string>();
        var map = AsMap(node) ?? throw new ConfigException($"{path}: expected a mapping of names to values");
        return map.ToDictionary(kv => kv.Key, kv => Str(kv.Value) ?? "", StringComparer.Ordinal);
    }

    /// <summary>A command is either a string or a mapping of platform to string.</summary>
    private static string ResolveCommand(object? node, OSKind os, string path)
    {
        if (node is null) throw new ConfigException($"{path}: missing 'run'");

        if (AsMap(node) is { } map)
        {
            foreach (var key in map.Keys)
                if (!PlatformKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                    throw new ConfigException(
                        $"{path}: unknown platform key '{key}' - expected one of {string.Join(", ", PlatformKeys)}");

            var wanted = os.Key();
            var match = map.FirstOrDefault(kv => string.Equals(kv.Key, wanted, StringComparison.OrdinalIgnoreCase));
            if (match.Key is null)
                throw new ConfigException($"{path}: no command for platform '{wanted}'");
            return Str(match.Value) ?? throw new ConfigException($"{path}: empty command for platform '{wanted}'");
        }

        return Str(node) ?? throw new ConfigException($"{path}: expected a command string");
    }

    // ---- sections -----------------------------------------------------------

    private static IReadOnlyList<Step> ParseSteps(object? node, OSKind os, string path)
    {
        if (node is null) return Array.Empty<Step>();
        var list = AsList(node) ?? throw new ConfigException($"{path}: expected a list of commands");

        var steps = new List<Step>();
        for (var i = 0; i < list.Count; i++)
        {
            var itemPath = $"{path}[{i}]";
            if (AsMap(list[i]) is { } map)
            {
                RejectUnknown(map, StepKeys, itemPath);
                steps.Add(new Step(
                    ResolveCommand(map.GetValueOrDefault("run"), os, itemPath),
                    Str(map.GetValueOrDefault("cwd")),
                    StringList(map.GetValueOrDefault("when")),
                    ParseBool(map.GetValueOrDefault("continueOnError"), $"{itemPath}.continueOnError")));
            }
            else
            {
                steps.Add(new Step(Str(list[i]) ?? "", null, Array.Empty<string>(), false));
            }
        }
        return steps;
    }

    private static IReadOnlyList<TaskDef> ParseTasks(Dictionary<string, object?> root, OSKind os)
    {
        var hasRun = root.ContainsKey("run");
        var hasTasks = root.ContainsKey("tasks");

        if (hasRun && hasTasks) throw new ConfigException("use either 'run' or 'tasks', not both");

        if (hasRun)
            return new[] { Task("run", ResolveCommand(root["run"], os, "run"), null) };

        if (!hasTasks) return Array.Empty<TaskDef>();

        var list = AsList(root["tasks"]) ?? throw new ConfigException("tasks: expected a list");
        var tasks = new List<TaskDef>();

        for (var i = 0; i < list.Count; i++)
        {
            var path = $"tasks[{i}]";
            if (AsMap(list[i]) is { } map)
            {
                RejectUnknown(map, TaskKeys, path);
                var (openReady, openUrl) = ParseOpen(map.GetValueOrDefault("open"), $"{path}.open");
                tasks.Add(new TaskDef(
                    Name: Str(map.GetValueOrDefault("name")) ?? $"task-{i + 1}",
                    Run: ResolveCommand(map.GetValueOrDefault("run"), os, path),
                    Cwd: Str(map.GetValueOrDefault("cwd")),
                    Env: ParseStringMap(map.GetValueOrDefault("env"), $"{path}.env"),
                    DependsOn: StringList(map.GetValueOrDefault("dependsOn")),
                    ReadyWhen: ParseReadyWhen(map.GetValueOrDefault("readyWhen"), $"{path}.readyWhen"),
                    OpenReady: openReady,
                    OpenUrl: openUrl,
                    Restart: ParseRestart(map.GetValueOrDefault("restart"), $"{path}.restart")));
            }
            else
            {
                tasks.Add(Task($"task-{i + 1}", Str(list[i]) ?? "", null));
            }
        }
        return tasks;

        static TaskDef Task(string name, string run, string? cwd) => new(
            name, run, cwd, new Dictionary<string, string>(), Array.Empty<string>(),
            null, false, null, RestartPolicy.Never);
    }

    private static ReadyWhen? ParseReadyWhen(object? node, string path)
    {
        if (node is null) return null;
        var map = AsMap(node) ?? throw new ConfigException($"{path}: expected a mapping");
        RejectUnknown(map, ReadyWhenKeys, path);

        if (map.Count != 1)
            throw new ConfigException(
                $"{path}: use exactly one of {string.Join(", ", ReadyWhenKeys)}, got {map.Count}");

        return new ReadyWhen(
            Port: ParseInt(map.GetValueOrDefault("port"), $"{path}.port"),
            Http: Str(map.GetValueOrDefault("http")),
            Log: Str(map.GetValueOrDefault("log")),
            Delay: map.TryGetValue("delay", out var delay) ? ParseDuration(Str(delay) ?? "", $"{path}.delay") : null);
    }

    private static TimeSpan ParseDuration(string text, string path)
    {
        var match = DurationPattern().Match(text.Trim());
        if (!match.Success)
            throw new ConfigException($"{path}: expected a duration like 500ms, 5s or 2m, got '{text}'");

        var value = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        return match.Groups[2].Value.ToLowerInvariant() switch
        {
            "ms" => TimeSpan.FromMilliseconds(value),
            "m" => TimeSpan.FromMinutes(value),
            _ => TimeSpan.FromSeconds(value),
        };
    }

    private static (bool OpenReady, string? OpenUrl) ParseOpen(object? node, string path)
    {
        if (node is null) return (false, null);
        var text = Str(node)!;
        if (bool.TryParse(text, out var flag)) return (flag, null);
        return (false, text);
    }

    private static RestartPolicy ParseRestart(object? node, string path)
    {
        var text = Str(node);
        if (string.IsNullOrWhiteSpace(text)) return RestartPolicy.Never;
        return text.ToLowerInvariant() switch
        {
            "never" => RestartPolicy.Never,
            "onfailure" => RestartPolicy.OnFailure,
            _ => throw new ConfigException($"{path}: expected never or onFailure, got '{text}'"),
        };
    }

    private static IReadOnlyList<ToolRequirement> ParseRequires(object? node)
    {
        if (node is null) return Array.Empty<ToolRequirement>();
        var list = AsList(node) ?? throw new ConfigException("requires: expected a list");

        var requirements = new List<ToolRequirement>();
        for (var i = 0; i < list.Count; i++)
        {
            var path = $"requires[{i}]";
            if (AsMap(list[i]) is { } map)
            {
                RejectUnknown(map, RequireKeys, path);
                var tool = Str(map.GetValueOrDefault("tool"))
                    ?? throw new ConfigException($"{path}: missing 'tool'");
                requirements.Add(new ToolRequirement(
                    tool,
                    Str(map.GetValueOrDefault("version")),
                    Str(map.GetValueOrDefault("install")),
                    ParseBool(map.GetValueOrDefault("optional"), $"{path}.optional")));
            }
            else
            {
                requirements.Add(new ToolRequirement(Str(list[i]) ?? "", null, null, false));
            }
        }
        return requirements;
    }

    private static IReadOnlyList<InputDef> ParseInputs(object? node)
    {
        if (node is null) return Array.Empty<InputDef>();
        var list = AsList(node) ?? throw new ConfigException("inputs: expected a list");

        var inputs = new List<InputDef>();
        for (var i = 0; i < list.Count; i++)
        {
            var path = $"inputs[{i}]";
            var map = AsMap(list[i]) ?? throw new ConfigException($"{path}: expected a mapping");
            RejectUnknown(map, InputKeys, path);

            var id = Str(map.GetValueOrDefault("id")) ?? throw new ConfigException($"{path}: missing 'id'");
            if (!IdentifierPattern().IsMatch(id))
                throw new ConfigException($"{path}: id '{id}' must be a letters-digits-underscore identifier");

            inputs.Add(new InputDef(
                Id: id,
                Label: Str(map.GetValueOrDefault("label")),
                Type: ParseInputType(Str(map.GetValueOrDefault("type")), $"{path}.type"),
                Description: Str(map.GetValueOrDefault("description")),
                Default: Str(map.GetValueOrDefault("default")),
                Required: ParseBool(map.GetValueOrDefault("required"), $"{path}.required"),
                Pattern: Str(map.GetValueOrDefault("pattern")),
                Min: ParseDouble(map.GetValueOrDefault("min"), $"{path}.min"),
                Max: ParseDouble(map.GetValueOrDefault("max"), $"{path}.max"),
                Options: ParseOptions(map.GetValueOrDefault("options"), $"{path}.options"),
                Env: Str(map.GetValueOrDefault("env")),
                Persist: ParseBool(map.GetValueOrDefault("persist"), $"{path}.persist")));
        }
        return inputs;
    }

    private static InputType ParseInputType(string? text, string path)
    {
        if (string.IsNullOrWhiteSpace(text)) return InputType.Text;
        if (Enum.TryParse<InputType>(text, ignoreCase: true, out var type)) return type;
        throw new ConfigException(
            $"{path}: unknown type '{text}' - expected one of {string.Join(", ", Enum.GetNames<InputType>()).ToLowerInvariant()}");
    }

    private static IReadOnlyList<InputOption> ParseOptions(object? node, string path)
    {
        if (node is null) return Array.Empty<InputOption>();
        var list = AsList(node) ?? throw new ConfigException($"{path}: expected a list");

        return list.Select((item, i) =>
        {
            if (AsMap(item) is { } map)
            {
                RejectUnknown(map, new[] { "value", "label" }, $"{path}[{i}]");
                var value = Str(map.GetValueOrDefault("value"))
                    ?? throw new ConfigException($"{path}[{i}]: missing 'value'");
                return new InputOption(value, Str(map.GetValueOrDefault("label")));
            }
            return new InputOption(Str(item) ?? "", null);
        }).ToList();
    }

    [GeneratedRegex(@"^(\d+(?:\.\d+)?)(ms|s|m)?$", RegexOptions.IgnoreCase)]
    private static partial Regex DurationPattern();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex IdentifierPattern();
}
