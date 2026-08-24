using System.ComponentModel;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using QuickRun.App.Daemon;
using QuickRun.Core;
using QuickRun.Core.Update;
using QuickRun.Core.Workspace;
using Spectre.Console.Cli;

namespace QuickRun.App.Commands;

/// <summary>
/// What running QuickRun with no arguments does: start the listener, put an icon in the tray and
/// open the dashboard. Printing CLI help into a console window that immediately closes is the worst
/// possible thing to do when someone double-clicks the binary.
/// </summary>
public sealed class UiCommand : AsyncCommand<UiCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-p|--port")]
        [Description("Port to listen on. Loopback only.")]
        public int Port { get; init; } = DaemonHost.DefaultPort;

        [CommandOption("--no-browser")]
        [Description("Do not open the dashboard in a browser.")]
        public bool NoBrowser { get; init; }

        [CommandOption("--no-tray")]
        [Description("Do not show a tray icon. The dashboard is then the only way back in.")]
        public bool NoTray { get; init; }

        [CommandOption("--no-update")]
        [Description("Do not check for updates.")]
        public bool NoUpdate { get; init; }
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var store = new WorkspaceStore();
        Directory.CreateDirectory(store.Root);

        if (Environment.ProcessPath is { } self) Updater.CleanUpAfterSwap(self);

        var pairing = new Pairing(store.Root);
        var url = $"http://127.0.0.1:{settings.Port}";

        // Another instance already owns the port: open its dashboard instead of failing with a
        // bind error nobody can act on.
        if (await AlreadyRunningAsync(settings.Port))
        {
            Output.Info($"QuickRun is already running on {url}");
            if (!settings.NoBrowser) Launch(url);
            return 0;
        }

        var app = DaemonHost.Build(settings.Port, store, pairing);

        try
        {
            await app.StartAsync(cancellationToken);
        }
        catch (Exception e) when (e is IOException or HttpRequestException)
        {
            Output.Error($"could not listen on port {settings.Port}: {e.Message}");
            return 1;
        }

        Output.Info($"QuickRun {BuildInfo.Version} listening on {url}");
        if (!settings.NoUpdate) _ = ReportUpdateAsync(store);
        if (!settings.NoBrowser) Launch(url);

        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        if (settings.NoTray)
        {
            await WaitForShutdownAsync(app, shutdown.Token);
        }
        else
        {
            TrayApp.Tooltip = $"QuickRun {BuildInfo.Version} — {url}";
            TrayApp.OpenDashboard = () => Launch(url);
            TrayApp.OpenPairing = () =>
            {
                pairing.OpenWindow();
                Launch(url);
            };
            TrayApp.Quit = shutdown.Cancel;

            // Blocks on this thread until Quit; Kestrel keeps serving in the background.
            TrayApp.Run(shutdown.Token);
        }

        await app.StopAsync(CancellationToken.None);
        return 0;
    }

    private static async Task<bool> AlreadyRunningAsync(int port)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(800) };
            using var response = await client.GetAsync($"http://127.0.0.1:{port}/api/ping");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static async Task WaitForShutdownAsync(WebApplication app, CancellationToken ct)
    {
        var stopping = new TaskCompletionSource();
        await using var registration = ct.Register(() => stopping.TrySetResult());
        app.Lifetime.ApplicationStopping.Register(() => stopping.TrySetResult());
        await stopping.Task;
    }

    private static async Task ReportUpdateAsync(WorkspaceStore store)
    {
        var source = InstallSources.DetectCurrent(store.Root);
        var status = await new UpdateChecker().CheckAsync(BuildInfo.Version, source);
        if (status.UpdateAvailable) Output.Warn(status.Advice);
    }

    internal static void Launch(string url)
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
