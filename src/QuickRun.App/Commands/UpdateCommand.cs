using System.ComponentModel;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using QuickRun.Core;
using QuickRun.Core.Update;
using QuickRun.Core.Workspace;
using Spectre.Console;
using Spectre.Console.Cli;

namespace QuickRun.App.Commands;

public sealed class UpdateCommand : AsyncCommand<UpdateCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--check")]
        [Description("Report whether an update exists and do nothing else.")]
        public bool CheckOnly { get; init; }

        [CommandOption("-y|--yes")]
        [Description("Install without asking.")]
        public bool Yes { get; init; }
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var executable = Environment.ProcessPath;
        if (executable is null)
        {
            Output.Error("cannot determine the running executable - update manually");
            return 1;
        }

        var source = InstallSources.DetectCurrent(new WorkspaceStore().Root);
        var checker = new UpdateChecker();
        var status = await checker.CheckAsync(BuildInfo.Version, source);

        if (status.Error is { } error)
        {
            Output.Error($"could not check for updates: {error}");
            return 1;
        }

        Output.Info($"installed {status.Current} ({source.ToString().ToLowerInvariant()})");

        if (!status.UpdateAvailable)
        {
            Output.Info("up to date");
            return 0;
        }

        // A package manager owns this binary: two updaters fighting over one file is how version
        // chaos starts, so report the command and stop.
        if (!source.MayReplaceItself())
        {
            Output.Warn(status.Advice);
            return 0;
        }

        Output.Info($"update available: {status.Latest}");
        if (settings.CheckOnly) return 0;

        if (!settings.Yes && !AnsiConsole.Confirm($"Install QuickRun {status.Latest}?", defaultValue: false))
        {
            Output.Info("cancelled");
            return 0;
        }

        var outcome = await SelfUpdate.RunAsync(executable, source, Output.Info, checker);

        if (outcome.Error is { } failure)
        {
            Output.Error(failure);
            return 1;
        }

        if (!outcome.Ok)
        {
            Output.Info("up to date");
            return 0;
        }

        Output.Info($"updated to {outcome.Version} - restart QuickRun to use it");
        return 0;
    }
}
