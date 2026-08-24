using System.Security.Cryptography;
using System.Text;
using QuickRun.Core.Config;

namespace QuickRun.Core.Run;

/// <summary>One command that will execute, with every placeholder already expanded.</summary>
public sealed record PlannedCommand(string Phase, string Name, string Command, string? Cwd);

/// <summary>
/// The auditable list of what a run will do. This is what the CLI prints before asking for
/// confirmation, what the desktop dialog renders, and what the trust store hashes.
/// </summary>
public sealed record RunPlan(
    string Repo,
    string Ref,
    string? Commit,
    string Workspace,
    string DisplayName,
    IReadOnlyList<PlannedCommand> Commands,
    IReadOnlyList<ToolRequirement> Requires,
    IReadOnlyList<InputDef> Inputs)
{
    /// <summary>
    /// Covers only the commands, deliberately not the repo, ref or commit: that is what lets
    /// "trust this repository" survive new commits but break when the commands change.
    /// </summary>
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\n", Commands.Select(c => $"{c.Phase}\t{c.Name}\t{c.Command}\t{c.Cwd}")))))
        .ToLowerInvariant();

    public string Describe()
    {
        var builder = new StringBuilder();
        builder.AppendLine(DisplayName);
        builder.AppendLine($"  repository  {Repo}");
        builder.AppendLine($"  ref         {Ref}{ShortCommit()}");
        builder.AppendLine($"  workspace   {Workspace}");
        builder.AppendLine();
        builder.AppendLine("These commands will run:");

        foreach (var group in Commands.GroupBy(c => c.Phase))
        {
            builder.AppendLine($"  {group.Key}:");
            foreach (var command in group)
                builder.AppendLine($"    {command.Command}"
                                   + (string.IsNullOrEmpty(command.Cwd) ? "" : $"   (in {command.Cwd})"));
        }

        return builder.ToString();
    }

    private string ShortCommit() =>
        Commit is null ? "" : $" ({Commit[..Math.Min(7, Commit.Length)]})";
}

public static class RunPlanBuilder
{
    public static RunPlan Build(RunConfig config, InterpolationContext ctx, OSKind os,
        string repo, string @ref, string? commit)
    {
        var platform = os.Key();
        var commands = new List<PlannedCommand>();

        AddSteps(config.Setup, "setup");
        foreach (var task in config.Tasks)
            commands.Add(new("task", task.Name, Interpolator.Expand(task.Run, ctx), Expand(task.Cwd, ctx)));
        AddSteps(config.Stop, "stop");

        return new RunPlan(repo, @ref, commit, ctx.Workspace, config.Name ?? ctx.RepoName,
            commands, config.Requires, config.Inputs);

        void AddSteps(IReadOnlyList<Step> steps, string phase)
        {
            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                // Platform-excluded steps are left out entirely: the list must show exactly what
                // will run on this machine, and nothing else.
                if (step.When.Count > 0 && !step.When.Contains(platform, StringComparer.OrdinalIgnoreCase))
                    continue;

                commands.Add(new(phase, $"{phase}-{i + 1}",
                    Interpolator.Expand(step.Run, ctx), Expand(step.Cwd, ctx)));
            }
        }
    }

    private static string? Expand(string? value, InterpolationContext ctx) =>
        value is null ? null : Interpolator.Expand(value, ctx);
}
