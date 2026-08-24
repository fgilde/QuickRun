using QuickRun.Core.Config;
using QuickRun.Core.Detect;
using QuickRun.Core.Run;
using QuickRun.Core.Workspace;
using Spectre.Console;

namespace QuickRun.App;

/// <summary>
/// Every console write in the application. Interpolated markup throughout, because repository
/// names and log lines contain '[' often enough that plain MarkupLine would throw.
/// </summary>
public static class Output
{
    public static void Info(string text) => AnsiConsole.MarkupLineInterpolated($"[grey]{text}[/]");

    public static void Warn(string text) => AnsiConsole.MarkupLineInterpolated($"[yellow]{text}[/]");

    public static void Error(string text) => AnsiConsole.MarkupLineInterpolated($"[red]{text}[/]");

    public static void Line(string text) => AnsiConsole.WriteLine(text);

    public static void Issues(IReadOnlyList<ValidationIssue> issues)
    {
        foreach (var issue in issues)
        {
            var severity = issue.IsError ? "[red]error[/]" : "[yellow]warning[/]";
            if (string.IsNullOrEmpty(issue.Path))
                AnsiConsole.MarkupLineInterpolated($"{severity} {issue.Message}");
            else
                AnsiConsole.MarkupLineInterpolated($"{severity} [grey]({issue.Path})[/] {issue.Message}");
        }
    }

    /// <summary>
    /// One line per progress change. Deliberately not a Spectre progress bar: a live bar and a
    /// streaming log fight over the same cursor, and a percent-prefixed line survives piping.
    /// </summary>
    public static void Progress(RunProgress progress) =>
        AnsiConsole.MarkupLineInterpolated($"[blue][[{progress.Percent,3}%]][/] [grey]{progress.Detail}[/]");

    public static void Plan(RunPlan plan)
    {
        AnsiConsole.Write(new Rule($"[bold]{Markup.Escape(plan.DisplayName)}[/]").LeftJustified());
        AnsiConsole.WriteLine(plan.Describe());
    }

    public static void Candidates(IReadOnlyList<Candidate> candidates)
    {
        var table = new Table().AddColumns("#", "kind", "directory", "commands");
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            table.AddRow(
                (i + 1).ToString(),
                Markup.Escape(candidate.Kind),
                Markup.Escape(candidate.RelativeDir.Length == 0 ? "." : candidate.RelativeDir),
                Markup.Escape(string.Join("\n", candidate.Setup.Concat(candidate.Run))));
        }
        AnsiConsole.Write(table);
    }

    public static void Workspaces(IReadOnlyList<WorkspaceInfo> workspaces)
    {
        var table = new Table().AddColumns("id", "repository", "ref", "size", "last used", "last run");
        foreach (var workspace in workspaces)
            table.AddRow(
                Markup.Escape(workspace.Id),
                Markup.Escape(workspace.Repo),
                Markup.Escape(workspace.Ref),
                Size(workspace.Bytes),
                workspace.LastUsed.LocalDateTime.ToString("yyyy-MM-dd HH:mm"),
                workspace.LastOk switch { true => "ok", false => "failed", null => "-" });
        AnsiConsole.Write(table);
    }

    public static string Size(long bytes) => bytes switch
    {
        > 1_073_741_824 => $"{bytes / 1_073_741_824.0:0.0} GB",
        > 1_048_576 => $"{bytes / 1_048_576.0:0.0} MB",
        > 1024 => $"{bytes / 1024.0:0.0} KB",
        _ => $"{bytes} B",
    };
}
