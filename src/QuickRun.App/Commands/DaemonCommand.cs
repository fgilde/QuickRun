using System.ComponentModel;
using Microsoft.AspNetCore.Builder;
using QuickRun.App.Daemon;
using QuickRun.Core;
using QuickRun.Core.Update;
using QuickRun.Core.Workspace;
using Spectre.Console.Cli;

namespace QuickRun.App.Commands;

/// <summary>
/// Runs the localhost listener. This is what the browser extension talks to, and the only way it
/// can tell that QuickRun is installed at all.
/// </summary>
public sealed class DaemonCommand : AsyncCommand<DaemonCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-p|--port")]
        [Description("Port to listen on. Loopback only.")]
        public int Port { get; init; } = DaemonHost.DefaultPort;

        [CommandOption("--pair")]
        [Description("Open a pairing window at startup, so an extension can collect its token.")]
        public bool Pair { get; init; }

        [CommandOption("--no-update")]
        [Description("Do not check for updates.")]
        public bool NoUpdate { get; init; }
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var store = new WorkspaceStore();
        Directory.CreateDirectory(store.Root);

        // Removes whatever a previous Windows self-update left behind.
        if (Environment.ProcessPath is { } self) Updater.CleanUpAfterSwap(self);

        var pairing = new Pairing(store.Root);
        if (settings.Pair)
        {
            pairing.OpenWindow();
            Output.Info($"pairing window open for {Pairing.WindowLength.TotalSeconds:0} seconds");
        }

        var app = DaemonHost.Build(settings.Port, store, pairing);

        Output.Info($"QuickRun {BuildInfo.Version} listening on http://127.0.0.1:{settings.Port}");
        if (!settings.NoUpdate) _ = ReportUpdateAsync();

        try
        {
            await app.StartAsync(cancellationToken);
            await WaitForShutdownAsync(app, cancellationToken);
            await app.StopAsync(CancellationToken.None);
            return 0;
        }
        catch (IOException e)
        {
            Output.Error($"could not listen on port {settings.Port}: {e.Message}");
            return 1;
        }
    }

    /// <summary>Runs until Ctrl+C or a host shutdown request.</summary>
    private static async Task WaitForShutdownAsync(WebApplication app, CancellationToken ct)
    {
        var stopping = new TaskCompletionSource();
        await using var registration = ct.Register(stopping.SetResult);
        app.Lifetime.ApplicationStopping.Register(() => stopping.TrySetResult());
        await stopping.Task;
    }

    private static async Task ReportUpdateAsync()
    {
        var source = InstallSources.DetectCurrent(new WorkspaceStore().Root);
        var status = await new UpdateChecker().CheckAsync(BuildInfo.Version, source);

        if (status.UpdateAvailable) Output.Warn(status.Advice);
    }
}

/// <summary>Opens the pairing window, so a browser extension can collect its token.</summary>
public sealed class PairCommand : Command<PairCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--revoke")]
        [Description("Invalidate the current token instead of pairing.")]
        public bool Revoke { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        // The window lives in a file, so this works whether or not a daemon is already running.
        var pairing = new Pairing(new WorkspaceStore().Root);

        if (settings.Revoke)
        {
            pairing.Reset();
            Output.Info("token revoked - pair again to reconnect the extension");
            return 0;
        }

        pairing.OpenWindow();
        Output.Info($"pairing window open for {Pairing.WindowLength.TotalSeconds:0} seconds");
        Output.Info("now open the QuickRun extension options and click Pair");
        return 0;
    }
}
