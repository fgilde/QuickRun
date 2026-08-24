using System.ComponentModel;
using System.Diagnostics;
using QuickRun.Core.Config;
using QuickRun.Core.Git;
using QuickRun.Core.Inputs;
using QuickRun.Core.Run;
using QuickRun.Core.Workspace;
using Spectre.Console;
using Spectre.Console.Cli;

namespace QuickRun.App.Commands;

public sealed class RunCommand : AsyncCommand<RunCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<repo>")]
        [Description("owner/repo, or a full repository URL.")]
        public string Repo { get; init; } = "";

        [CommandOption("-r|--ref")]
        [Description("Branch, tag or commit. Defaults to the repository's default branch.")]
        public string? Ref { get; init; }

        [CommandOption("-p|--pr")]
        [Description("Pull request number to run instead of a ref.")]
        public int? PullRequest { get; init; }

        [CommandOption("-d|--subdir")]
        [Description("Subdirectory to treat as the project root.")]
        public string? Subdir { get; init; }

        [CommandOption("-i|--input")]
        [Description("Fill a declared input: --input key=value. Repeatable.")]
        public string[] Inputs { get; init; } = Array.Empty<string>();

        [CommandOption("-t|--token")]
        [Description("Access token for a private repository.")]
        public string? Token { get; init; }

        [CommandOption("-c|--config")]
        [Description("Config file to use instead of quickrun.yml, relative to the project root.")]
        public string? ConfigPath { get; init; }

        [CommandOption("--fresh")]
        [Description("Delete the existing workspace and clone again.")]
        public bool Fresh { get; init; }

        [CommandOption("-y|--yes")]
        [Description("Skip the confirmation prompt. Missing required inputs then fail instead of prompting.")]
        public bool Yes { get; init; }

        [CommandOption("--no-open")]
        [Description("Do not open any browser URL the config asks for.")]
        public bool NoOpen { get; init; }

        public RunArgs ToArgs() =>
            new(Repo, Ref, PullRequest, Subdir, Inputs, Token, Fresh, Yes, NoOpen, ConfigPath);
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var store = new WorkspaceStore();
        var git = new GitClient(new CredentialResolver(settings.Token));
        var args = settings.ToArgs();

        var preparation = RunPipeline.Prepare(args, store, git,
            settings.Yes
                ? (_, provided) => provided
                : Prompts.Collect);

        if (preparation.ExitCode != 0)
        {
            if (preparation.Error is { } error) Output.Error(error);
            return preparation.ExitCode;
        }

        var plan = preparation.Plan!;
        var config = preparation.Config!;

        Output.Plan(plan);

        foreach (var candidate in preparation.OtherCandidates)
            Output.Info($"also detected: {candidate.Label} - use --config or commit a quickrun.yml to choose");

        foreach (var conflict in PortScan.Occupied(config))
            Output.Warn($"port {conflict.Port} (needed by task '{conflict.Task}') is already in use - "
                        + "the readiness check may match another application");

        if (!settings.Yes && !AnsiConsole.Confirm("Run these commands?", defaultValue: false))
        {
            Output.Info("cancelled");
            return 0;
        }

        var secrets = Interpolator.Secrets(preparation.Values!, InputResolver.SecretIds(config.Inputs));
        var options = new RunOptions(
            preparation.Workspace!,
            new InterpolationContext(preparation.Values!, preparation.Workspace!,
                RunPipeline.RepoName(plan.Repo), plan.Ref),
            InputResolver.ToEnv(config.Inputs, preparation.Values!),
            secrets,
            Readiness.DefaultTimeout);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Output.Info("stopping...");
            cts.Cancel();
        };

        await using var runner = new Runner(e => Report(e, settings.NoOpen));
        var outcome = await runner.ExecuteAsync(config, options, cts.Token);
        await runner.StopAsync();

        store.Touch(WorkspaceStore.IdFor(plan.Repo, plan.Ref), plan.Repo, plan.Ref, plan.Commit, outcome.Ok);

        if (!outcome.Ok) Output.Error(outcome.Error ?? "run failed");
        return outcome.Ok ? 0 : 1;
    }

    /// <summary>The one place in QuickRun that opens a browser. Core only ever reports the URL.</summary>
    private static void Report(RunEvent e, bool noOpen)
    {
        var prefix = e.Task is null ? "" : $"[{e.Task}] ";

        switch (e.Kind)
        {
            case RunEventKind.Output:
                Output.Line(prefix + e.Text);
                break;
            case RunEventKind.Error:
                Output.Warn(prefix + e.Text);
                break;
            case RunEventKind.Failed:
                Output.Error(prefix + e.Text);
                break;
            case RunEventKind.Info when e.Text.StartsWith("open ", StringComparison.Ordinal):
                var url = e.Text["open ".Length..];
                Output.Info($"{prefix}{url}");
                if (!noOpen) Launch(url);
                break;
            default:
                Output.Info(prefix + e.Text);
                break;
        }
    }

    private static void Launch(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Output.Warn($"could not open {url}: {e.Message}");
        }
    }
}
