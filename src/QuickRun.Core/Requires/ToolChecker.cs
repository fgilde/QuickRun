using QuickRun.Core.Config;
using QuickRun.Core.Process;

namespace QuickRun.Core.Requires;

public sealed record ToolCheckResult(ToolRequirement Requirement, bool Found, string? FoundVersion, bool Satisfied)
{
    /// <summary>An unsatisfied requirement only blocks a run when it is not optional.</summary>
    public bool Blocks => !Satisfied && !Requirement.Optional;

    public string Describe()
    {
        var wanted = string.IsNullOrWhiteSpace(Requirement.Version) ? "" : " " + Requirement.Version;
        if (!Found) return $"{Requirement.Tool}{wanted} - not installed";
        return $"{Requirement.Tool}{wanted} - found {FoundVersion ?? "present"}";
    }
}

public static class ToolChecker
{
    private static readonly Dictionary<string, string> KnownProbes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["node"] = "-v",
        ["npm"] = "-v",
        ["pnpm"] = "-v",
        ["yarn"] = "-v",
        ["bun"] = "-v",
        ["java"] = "-version",
        ["go"] = "version",
        ["mvn"] = "-v",
    };

    public static string[] ProbeArgs(string tool) => new[] { KnownProbes.GetValueOrDefault(tool, "--version") };

    /// <summary>
    /// Probes one requirement. The runner is injectable so tests do not depend on which tools the
    /// machine happens to have; the default goes through the shell so .cmd shims resolve on Windows.
    /// </summary>
    public static ToolCheckResult Check(ToolRequirement requirement, Func<string, string[], CommandResult>? runner = null)
    {
        runner ??= ShellProbe;
        var result = runner(requirement.Tool, ProbeArgs(requirement.Tool));

        if (result.ExitCode != 0) return new(requirement, false, null, false);

        var version = VersionCheck.Extract(result.Output);
        var satisfied = string.IsNullOrWhiteSpace(requirement.Version)
                        || VersionCheck.Satisfies(version, requirement.Version);
        return new(requirement, true, version, satisfied);
    }

    public static IReadOnlyList<ToolCheckResult> CheckAll(IEnumerable<ToolRequirement> requirements) =>
        requirements.Select(r => Check(r)).ToList();

    private static CommandResult ShellProbe(string tool, string[] args)
    {
        var (shell, shellArgs) = ShellCommand.Resolve($"{tool} {string.Join(' ', args)}");
        return CommandRunner.Capture(shell, shellArgs, timeoutMs: 15_000);
    }
}
