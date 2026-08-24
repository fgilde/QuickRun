namespace QuickRun.Core.Config;

public enum InputType
{
    Text,
    Password,
    Number,
    Bool,
    Select,
    Path,
    Dir,
    File,
}

public enum RestartPolicy
{
    Never,
    OnFailure,
}

/// <summary>A prerequisite the machine must satisfy before a run starts.</summary>
public sealed record ToolRequirement(string Tool, string? Version, string? Install, bool Optional);

public sealed record InputOption(string Value, string? Label);

/// <summary>One field of the form QuickRun generates before a run.</summary>
public sealed record InputDef(
    string Id,
    string? Label,
    InputType Type,
    string? Description,
    string? Default,
    bool Required,
    string? Pattern,
    double? Min,
    double? Max,
    IReadOnlyList<InputOption> Options,
    string? Env,
    bool Persist);

/// <summary>A sequential command, used by both <c>setup</c> and <c>stop</c>.</summary>
public sealed record Step(string Run, string? Cwd, IReadOnlyList<string> When, bool ContinueOnError);

/// <summary>Exactly one of these is set; all null means "ready once the process started".</summary>
public sealed record ReadyWhen(int? Port, string? Http, string? Log, TimeSpan? Delay);

/// <summary>A long-running process.</summary>
public sealed record TaskDef(
    string Name,
    string Run,
    string? Cwd,
    IReadOnlyDictionary<string, string> Env,
    IReadOnlyList<string> DependsOn,
    ReadyWhen? ReadyWhen,
    bool OpenReady,
    string? OpenUrl,
    RestartPolicy Restart);

/// <summary>A parsed quickrun.yml, with all shorthand already expanded.</summary>
public sealed record RunConfig(
    int Version,
    string? Name,
    string? Description,
    string? Icon,
    string? Docs,
    IReadOnlyList<ToolRequirement> Requires,
    IReadOnlyList<InputDef> Inputs,
    IReadOnlyDictionary<string, string> Env,
    IReadOnlyList<Step> Setup,
    IReadOnlyList<TaskDef> Tasks,
    IReadOnlyList<Step> Stop);

public static class RunConfigDefaults
{
    public static RunConfig Empty => new(1, null, null, null, null,
        Array.Empty<ToolRequirement>(), Array.Empty<InputDef>(),
        new Dictionary<string, string>(), Array.Empty<Step>(),
        Array.Empty<TaskDef>(), Array.Empty<Step>());
}

public sealed class ConfigException(string message) : Exception(message);
