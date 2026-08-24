using System.ComponentModel;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using QuickRun.App.Daemon;
using QuickRun.App.Ui;
using QuickRun.Core;
using QuickRun.Core.Update;
using QuickRun.Core.Run;
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

        [CommandOption("--browser")]
        [Description("Open the dashboard in a browser instead of the desktop window.")]
        public bool Browser { get; init; }

        [CommandOption("--no-window")]
        [Description("Do not open any window at startup. The tray icon still opens one on demand.")]
        public bool NoWindow { get; init; }

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
            // Its tray icon owns the window; the browser is the only thing this process can offer.
            Output.Info($"QuickRun is already running on {url}");
            Launch(url);
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

        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Without a tray there is no Avalonia loop to host a window, so the browser is the UI.
        if (settings.NoTray)
        {
            if (!settings.NoWindow) Launch(url);
            await WaitForShutdownAsync(app, shutdown.Token);
        }
        else
        {
            var registry = app.Services.GetRequiredService<RunRegistry>();

            TrayApp.Tooltip = $"QuickRun {BuildInfo.Version} - {url}";
            TrayApp.OpenDashboard = () => AppWindows.Show(registry, store, pairing, url);
            TrayApp.OpenInBrowser = () => Launch(url);
            TrayApp.OpenPairing = () =>
            {
                pairing.OpenWindow();
                AppWindows.Show(registry, store, pairing, url);
            };
            TrayApp.Quit = shutdown.Cancel;

            if (!settings.NoWindow)
                TrayApp.Started = settings.Browser
                    ? () => Launch(url)
                    : () => AppWindows.Show(registry, store, pairing, url);

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
