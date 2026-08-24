using System.ComponentModel;
using QuickRun.App.Daemon;
using Spectre.Console.Cli;

namespace QuickRun.App.Commands;

public sealed class InstallCommand : Command<InstallCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-p|--port")]
        [Description("Port the autostarted daemon should listen on.")]
        public int Port { get; init; } = DaemonHost.DefaultPort;
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var executable = Environment.ProcessPath;
        if (executable is null)
        {
            Output.Error("cannot determine the running executable");
            return 1;
        }

        var steps = SystemIntegration.Install(executable, settings.Port);
        Report(steps);

        Output.Info("");
        Output.Info("next: quickrun pair    # then click Pair in the browser extension");

        // A failed step is worth reporting but does not make the install useless: the CLI works
        // either way, and so does the listener once it is started by hand.
        return steps.All(s => s.Ok) ? 0 : 1;
    }

    internal static void Report(IReadOnlyList<IntegrationStep> steps)
    {
        foreach (var step in steps)
        {
            if (step.Ok) Output.Info($"{step.What}: {step.Detail}");
            else Output.Warn($"{step.What} failed: {step.Detail}");
        }
    }
}

public sealed class UninstallCommand : Command<UninstallCommand.Settings>
{
    public sealed class Settings : CommandSettings;

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var steps = SystemIntegration.Uninstall();
        InstallCommand.Report(steps);

        Output.Info("");
        Output.Info("workspaces are kept - remove them with: quickrun clean --all");

        return steps.All(s => s.Ok) ? 0 : 1;
    }
}

/// <summary>
/// Handles a <c>quickrun://</c> URL. Registered by <see cref="InstallCommand"/> as the scheme's
/// handler, and invoked by the OS when the extension opens such a link.
/// </summary>
public sealed class HandleCommand : AsyncCommand<HandleCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<url>")]
        [Description("A quickrun:// URL.")]
        public string Url { get; init; } = "";

        [CommandOption("-p|--port")]
        public int Port { get; init; } = DaemonHost.DefaultPort;
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(settings.Url, UriKind.Absolute, out var url)
            || !string.Equals(url.Scheme, "quickrun", StringComparison.OrdinalIgnoreCase))
        {
            Output.Error($"not a quickrun:// URL: {settings.Url}");
            return 2;
        }

        // The scheme's only job is starting the daemon; the extension then talks to the listener,
        // which is the channel that can report progress and be confirmed against.
        Output.Info($"starting the QuickRun daemon on port {settings.Port}");

        var daemon = new DaemonCommand();
        return await daemon.RunAsync(new DaemonCommand.Settings { Port = settings.Port }, cancellationToken);
    }
}
