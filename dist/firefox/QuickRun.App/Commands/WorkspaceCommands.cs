using System.ComponentModel;
using System.Text.RegularExpressions;
using QuickRun.Core.Workspace;
using Spectre.Console;
using Spectre.Console.Cli;

namespace QuickRun.App.Commands;

public sealed record CleanRequest(bool All, TimeSpan? OlderThan, string? Id);

public sealed record CleanResult(int ExitCode, int Removed, string? Error);

public static partial class WorkspaceOps
{
    public static TimeSpan? ParseAge(string? text)
    {
        if (text is null) return null;

        var match = AgePattern().Match(text.Trim());
        if (!match.Success)
            throw new ArgumentException($"expected a duration like 30d, 12h or 2w, got '{text}'");

        var count = int.Parse(match.Groups[1].Value);
        return match.Groups[2].Value.ToLowerInvariant() switch
        {
            "h" => TimeSpan.FromHours(count),
            "w" => TimeSpan.FromDays(7 * count),
            _ => TimeSpan.FromDays(count),
        };
    }

    /// <summary>
    /// Exactly one selector must be given. Cleaning everything by default would be the worst
    /// possible guess, so no selector is a usage error rather than a silent no-op.
    /// </summary>
    public static CleanResult Clean(WorkspaceStore store, CleanRequest request)
    {
        var selectors = new[] { request.All, request.OlderThan is not null, request.Id is not null }
            .Count(given => given);

        if (selectors != 1)
            return new(2, 0, "specify exactly one of --all, --older-than <age> or a workspace id");

        if (request.All) return new(0, store.RemoveAll(), null);
        if (request.OlderThan is { } age) return new(0, store.Clean(age), null);

        try
        {
            return store.Remove(request.Id!)
                ? new(0, 1, null)
                : new(1, 0, $"no workspace with id '{request.Id}'");
        }
        catch (ArgumentException e)
        {
            return new(1, 0, e.Message);
        }
    }

    [GeneratedRegex(@"^(\d+)([hdw])$", RegexOptions.IgnoreCase)]
    private static partial Regex AgePattern();
}

public sealed class ListCommand : Command<ListCommand.Settings>
{
    public sealed class Settings : CommandSettings;

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var workspaces = new WorkspaceStore().List();
        if (workspaces.Count == 0)
        {
            Output.Info("no workspaces yet");
            return 0;
        }

        Output.Workspaces(workspaces);
        Output.Info($"{workspaces.Count} workspace(s), {Output.Size(workspaces.Sum(w => w.Bytes))} total");
        return 0;
    }
}

public sealed class CleanCommand : Command<CleanCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[id]")]
        [Description("Workspace id to remove, as shown by 'quickrun ls'.")]
        public string? Id { get; init; }

        [CommandOption("--all")]
        [Description("Remove every workspace.")]
        public bool All { get; init; }

        [CommandOption("--older-than")]
        [Description("Remove workspaces unused for longer than this, e.g. 30d, 12h, 2w.")]
        public string? OlderThan { get; init; }

        [CommandOption("-y|--yes")]
        [Description("Skip the confirmation prompt for --all.")]
        public bool Yes { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        TimeSpan? age;
        try { age = WorkspaceOps.ParseAge(settings.OlderThan); }
        catch (ArgumentException e) { Output.Error(e.Message); return 2; }

        if (settings.All && !settings.Yes && !AnsiConsole.Confirm("Delete every workspace?", defaultValue: false))
        {
            Output.Info("cancelled");
            return 0;
        }

        var result = WorkspaceOps.Clean(new WorkspaceStore(), new CleanRequest(settings.All, age, settings.Id));

        if (result.Error is { } error) Output.Error(error);
        else Output.Info($"removed {result.Removed} workspace(s)");

        return result.ExitCode;
    }
}
