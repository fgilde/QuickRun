using System.ComponentModel;
using System.Diagnostics;
using QuickRun.Core;
using QuickRun.Core.Config;
using QuickRun.Core.Git;
using QuickRun.Core.Inputs;
using QuickRun.Core.Requires;
using QuickRun.Core.Run;
using QuickRun.Core.Workspace;
using Spectre.Console;
using Spectre.Console.Cli;

namespace QuickRun.App.Commands;

public sealed class RunCommand : AsyncCommand<RunCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[repo]")]
        [Description("owner/repo, a full repository URL, or a folder on this machine.")]
        public string Repo { get; init; } = "";

        [CommandOption("--path")]
        [Description("Run this folder instead of checking a repository out. Nothing is copied.")]
        public string? LocalPath { get; init; }

        [CommandOption("--copy")]
        [Description("With --path: run a copy under the workspace directory, leaving the folder alone.")]
        public bool Copy { get; init; }

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

        [CommandOption("-f|--file")]
        [Description("Run this quickrun.yml on its own. It decides what runs; if it names a "
                     + "repository that is checked out, otherwise the file's own folder runs.")]
        public string? ConfigFile { get; init; }

        [CommandOption("--fresh")]
        [Description("Delete the existing workspace and clone again.")]
        public bool Fresh { get; init; }

        [CommandOption("-y|--yes")]
        [Description("Skip the confirmation prompt. Missing required inputs then fail instead of prompting.")]
        public bool Yes { get; init; }

        [CommandOption("--no-open")]
        [Description("Do not open any browser URL the config asks for.")]
        public bool NoOpen { get; init; }

        /// <summary>
        /// The folder to run, if that is what this is.
        /// <para>
        /// Either named with --path - which is what a shell verb passes, because there it must not
        /// be guessed - or given as the argument, so that `quickrun run .` does what anyone typing
        /// it means. A repository shorthand is never a directory that exists, so the two do not
        /// collide.
        /// </para>
        /// </summary>
        public string? Folder =>
            LocalPath ?? (Repo.Length > 0 && Directory.Exists(Repo) ? Repo : null);

        public RunArgs ToArgs() =>
            new(Repo, Ref, PullRequest, Subdir, Inputs, Token, Fresh, Yes, NoOpen, ConfigPath,
                LocalPath: Folder, Copy: Copy);

        /// <summary>
        /// The same run, described by a config file instead of by a repository.
        /// <para>
        /// The file is read here and handed on as text, so everything downstream - preparing,
        /// planning, confirming - is the path it always was. A folder is allowed: this is a command
        /// line, so the file was named by whoever owns the machine.
        /// </para>
        /// </summary>
        public (RunArgs? Args, string? Error) FromFile()
        {
            var target = ConfigFileRun.Read(ConfigFile, OSKinds.Current, allowFolder: true);

            if (target.Error is { } why) return (null, why);

            // A repository on the command line wins over the one in the file: somebody who names
            // both means the one they just typed.
            var repo = Repo.Length > 0 ? Repo : target.Repo ?? "";

            if (repo.Length == 0 && target.LocalFolder is null)
                return (null, "this config does not say which repository it is for - name one after --file");

            return (new RunArgs(repo, Ref ?? target.Ref, PullRequest, Subdir, Inputs, Token,
                Fresh, Yes, NoOpen, ConfigPath: null,
                ConfigText: target.Text,
                LocalPath: repo.Length > 0 ? null : target.LocalFolder,
                Copy: Copy), null);
        }
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var store = new WorkspaceStore();
        var reporter = new ProgressReporter();
        var git = new GitClient(
            new CredentialResolver(settings.Token),
            onCheckoutProgress: (percent, detail) =>
                reporter.Report(new RunProgress(RunPhase.Checkout, ProgressModel.Total(RunPhase.Checkout, percent), detail)));
        RunArgs args;

        if (settings.ConfigFile is { Length: > 0 })
        {
            var (fromFile, why) = settings.FromFile();

            if (fromFile is null)
            {
                Output.Error(why ?? "that config cannot be run");
                return 2;
            }

            args = fromFile;
        }
        else
        {
            args = settings.ToArgs();
        }

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

        foreach (var note in preparation.Notes)
            Output.Info(note);

        foreach (var candidate in preparation.OtherCandidates)
            Output.Info($"also detected: {candidate.Label} - use --config or commit a quickrun.yml to choose");

        // What is missing and would be installed, before the question is asked rather than after.
        foreach (var check in ToolChecker.CheckAll(config.Requires))
            if (Provisioner.PlanFor(check, Path.Combine(store.Root, "tools")) is { } provision)
                Output.Info($"{provision.Tool} {provision.Version} is missing - QuickRun will install "
                            + $"it into {provision.Directory} from {provision.Source}");

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
            Readiness.DefaultTimeout,
            // A missing toolchain is installed here rather than reported: everything QuickRun puts
            // on a machine lives under its own root, and this is where that root is.
            ToolRoot: Path.Combine(store.Root, "tools"));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Output.Info("stopping...");
            cts.Cancel();
        };

        await using var runner = new Runner(e => Report(e, settings.NoOpen, reporter));
        var outcome = await runner.ExecuteAsync(config, options, cts.Token);
        await runner.StopAsync();

        // The folder travels with the preparation, so recording the outcome does not turn a note
        // about somebody's working copy into a claim that QuickRun owns it.
        store.Touch(preparation.WorkspaceId!, plan.Repo, plan.Ref, plan.Commit,
            outcome.Ok, preparation.LocalFolder);

        if (!outcome.Ok) Output.Error(outcome.Error ?? "run failed");
        return outcome.Ok ? 0 : 1;
    }

    /// <summary>The one place in QuickRun that opens a browser. Core only ever reports the URL.</summary>
    private static void Report(RunEvent e, bool noOpen, ProgressReporter reporter)
    {
        var prefix = e.Task is null ? "" : $"[{e.Task}] ";

        switch (e.Kind)
        {
            case RunEventKind.Progress when e.Progress is { } progress:
                reporter.Report(progress);
                break;
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

    /// <summary>
    /// Prints one line per whole percent. Keying on the detail text instead would print every one
    /// of git's hundred "Counting objects" updates while the total sat at 0%.
    /// </summary>
    private sealed class ProgressReporter
    {
        private int _last = -1;

        public void Report(RunProgress progress)
        {
            lock (this)
            {
                if (progress.Percent == _last) return;
                _last = progress.Percent;
            }
            Output.Progress(progress);
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
