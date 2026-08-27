using System.Text.RegularExpressions;

namespace QuickRun.Core.Config;

public sealed record ValidationIssue(string Path, string Message, bool IsError);

/// <summary>
/// Rejects incoherent content: dependencies that point nowhere, cycles, duplicate names,
/// placeholders naming inputs that do not exist. Syntax is already the parser's business.
/// </summary>
public static class ConfigValidator
{
    private const int SupportedVersion = 1;

    private static readonly string[] PreservedRoots = { "node_modules", ".venv", "obj", "bin" };

    public static IReadOnlyList<ValidationIssue> Validate(RunConfig config)
    {
        var issues = new List<ValidationIssue>();
        void Error(string path, string message) => issues.Add(new(path, message, true));
        void Warn(string path, string message) => issues.Add(new(path, message, false));

        if (config.Version != SupportedVersion)
            Error("version",
                $"unsupported version {config.Version}, this build understands version {SupportedVersion}");

        if (config.Tasks.Count == 0 && config.Setup.Count == 0)
            Error("", "nothing to run - add a 'run' command or a 'tasks' list");

        ValidateTasks(config, Error, Warn);
        ValidateInputs(config, Error, Warn);
        ValidateInterpolation(config, Error);
        ValidatePaths(config, Error);

        return issues;
    }

    private static void ValidateTasks(RunConfig config, Action<string, string> error, Action<string, string> warn)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var task in config.Tasks)
            if (!names.Add(task.Name))
                error($"tasks.{task.Name}", $"duplicate task name '{task.Name}'");

        foreach (var task in config.Tasks)
            foreach (var dependency in task.DependsOn)
                if (!names.Contains(dependency))
                    error($"tasks.{task.Name}.dependsOn",
                        $"dependsOn references unknown task '{dependency}'");

        DetectCycles(config, error);

        var dependedOn = config.Tasks.SelectMany(t => t.DependsOn).ToHashSet(StringComparer.Ordinal);
        foreach (var task in config.Tasks.Where(t => dependedOn.Contains(t.Name) && t.ReadyWhen is null))
            warn($"tasks.{task.Name}",
                $"'{task.Name}' has no readyWhen, so dependants start as soon as it launches");
    }

    private static void DetectCycles(RunConfig config, Action<string, string> error)
    {
        // First wins: duplicate names are reported separately, and this must not throw on them.
        var byName = config.Tasks
            .DistinctBy(t => t.Name, StringComparer.Ordinal)
            .ToDictionary(t => t.Name, StringComparer.Ordinal);
        var done = new HashSet<string>(StringComparer.Ordinal);
        var reported = false;

        foreach (var task in config.Tasks)
        {
            if (reported) break;
            Walk(task.Name, new List<string>());
        }

        void Walk(string name, List<string> path)
        {
            if (reported) return;

            if (path.Contains(name, StringComparer.Ordinal))
            {
                var cycle = path.SkipWhile(n => n != name).Append(name);
                error("tasks", $"dependency cycle: {string.Join(" -> ", cycle)}");
                reported = true;
                return;
            }

            if (!done.Add(name) || !byName.TryGetValue(name, out var task)) return;

            path.Add(name);
            foreach (var dependency in task.DependsOn) Walk(dependency, path);
            path.RemoveAt(path.Count - 1);
        }
    }

    private static void ValidateInputs(RunConfig config, Action<string, string> error, Action<string, string> warn)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var input in config.Inputs)
        {
            var path = $"inputs.{input.Id}";

            if (!ids.Add(input.Id)) error(path, $"duplicate input id '{input.Id}'");

            if (input.Type == InputType.Select)
            {
                if (input.Options.Count == 0)
                    error(path, $"select input '{input.Id}' needs options");
                else if (input.Default is { } defaultValue
                         && !input.Options.Any(o => string.Equals(o.Value, defaultValue, StringComparison.Ordinal)))
                    error(path, $"default '{defaultValue}' is not one of the options");
            }

            if (!string.IsNullOrWhiteSpace(input.Pattern))
            {
                try { _ = new Regex(input.Pattern); }
                catch (ArgumentException e) { error(path, $"invalid pattern: {e.Message}"); }

                if (input.Type is not (InputType.Text or InputType.Password
                    or InputType.Path or InputType.Dir or InputType.File))
                    warn(path, $"pattern is ignored for type {input.Type.ToString().ToLowerInvariant()}");
            }

            if (input.Type != InputType.Number && (input.Min is not null || input.Max is not null))
                warn(path, $"min and max are ignored for type {input.Type.ToString().ToLowerInvariant()}");

            if (input.Min is { } min && input.Max is { } max && min > max)
                error(path, $"min {min} is greater than max {max}");
        }
    }

    private static void ValidateInterpolation(RunConfig config, Action<string, string> error)
    {
        var known = config.Inputs.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var (path, text) in Interpolatable(config))
        {
            foreach (var placeholder in Interpolator.Placeholders(text))
            {
                if (placeholder is "workspace" or "repo.name" or "repo.ref") continue;

                var parts = placeholder.Split('.', 2);
                if (parts.Length != 2)
                {
                    error(path, $"unknown placeholder '${{{placeholder}}}'");
                    continue;
                }

                switch (parts[0])
                {
                    case "inputs" when !known.Contains(parts[1]):
                        error(path, $"unknown input reference '{parts[1]}'");
                        break;
                    case "inputs":
                    case "env":
                        break;
                    default:
                        error(path, $"unknown placeholder namespace '{parts[0]}'");
                        break;
                }
            }
        }
    }

    private static IEnumerable<(string Path, string Text)> Interpolatable(RunConfig config)
    {
        foreach (var kv in config.Env) yield return ($"env.{kv.Key}", kv.Value);

        foreach (var (steps, phase) in new[] { (config.Setup, "setup"), (config.Stop, "stop") })
            for (var i = 0; i < steps.Count; i++)
            {
                yield return ($"{phase}[{i}]", steps[i].Run);
                if (steps[i].Cwd is { } cwd) yield return ($"{phase}[{i}].cwd", cwd);
            }

        foreach (var task in config.Tasks)
        {
            yield return ($"tasks.{task.Name}", task.Run);
            if (task.Cwd is { } cwd) yield return ($"tasks.{task.Name}.cwd", cwd);
            if (task.OpenUrl is { } url) yield return ($"tasks.{task.Name}.open", url);
            if (task.ReadyWhen?.Http is { } http) yield return ($"tasks.{task.Name}.readyWhen.http", http);
            if (task.ReadyWhen?.Log is { } pattern) yield return ($"tasks.{task.Name}.readyWhen.log", pattern);
            foreach (var kv in task.Env) yield return ($"tasks.{task.Name}.env.{kv.Key}", kv.Value);
        }
    }

    private static void ValidatePaths(RunConfig config, Action<string, string> error)
    {
        foreach (var (path, cwd) in Cwds(config))
        {
            if (string.IsNullOrWhiteSpace(cwd)) continue;

            if (Path.IsPathRooted(cwd))
            {
                error(path, "cwd must be relative to the repository root");
                continue;
            }

            // No file system access: a fake root is enough to catch traversal.
            const string fakeRoot = "/quickrun-root";
            var combined = Path.GetFullPath(Path.Combine(fakeRoot, cwd));
            var root = Path.GetFullPath(fakeRoot);
            if (!combined.StartsWith(root, StringComparison.Ordinal))
                error(path, "cwd points outside the repository");
        }
    }

    private static IEnumerable<(string Path, string? Cwd)> Cwds(RunConfig config)
    {
        foreach (var (steps, phase) in new[] { (config.Setup, "setup"), (config.Stop, "stop") })
            for (var i = 0; i < steps.Count; i++)
                yield return ($"{phase}[{i}].cwd", steps[i].Cwd);

        foreach (var task in config.Tasks)
            yield return ($"tasks.{task.Name}.cwd", task.Cwd);
    }
}
