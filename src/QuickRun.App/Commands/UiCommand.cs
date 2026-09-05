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

        var url = $"http://127.0.0.1:{settings.Port}";

        // One QuickRun per machine. A second start is not an error and not a second instance: it
        // asks the one that exists to show itself and leaves. Otherwise every quickrun:// link and
        // every double-click would add another listener fighting for the same port, and runs would
        // be spread across instances nobody can see.
        if (await SingleInstance.RunningAsync(settings.Port, cancellationToken))
        {
            Output.Info($"QuickRun is already running on {url}");

            // Only the browser is left when the running one is headless or older than /api/show.
            if (!await SingleInstance.ShowAsync(settings.Port, target: null, cancellationToken))
                Launch(url);

            return 0;
        }

        var app = await ListenAsync(settings.Port, store, cancellationToken);
        if (app is null) return 1;

        Output.Info($"QuickRun {BuildInfo.Version} listening on {url}");
        if (!settings.NoUpdate) _ = ReportUpdateAsync(store);

        // A quickrun:// link is only as good as what it points at, and what it points at is written
        // once at install time. Anyone who unpacks a new build somewhere else - which is what
        // downloading a release does - leaves the registration aimed at a binary that has moved or
        // gone, and then clicking a badge does nothing at all, silently. So the running build makes
        // the registration point at itself.
        RepairIntegration(settings.Port);

        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Set by the update endpoint once the new binary is in place: stop, then start the file
        // that is now on disk. Quitting and coming back is what makes an update take effect, and
        // asking the user to do it by hand is asking them to run an old build until they remember.
        var control = app.Services.GetRequiredService<DaemonHost.HostControl>();

        var restarting = false;
        control.Restart = () =>
        {
            restarting = true;
            shutdown.Cancel();
        };

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
            TrayApp.OpenDashboard = () => AppWindows.Show(registry, store, url);
            TrayApp.OpenInBrowser = () => Launch(url);
            TrayApp.Quit = shutdown.Cancel;

            // What a second start, and every quickrun:// link, reaches: this window, raised, on the
            // repository the link named.
            control.ShowWindow = hash => AppWindows.ShowTarget(registry, store, url, hash);

            // The page asks for a folder or a config; both pickers belong to the window, which is here.
            control.PickFolder = AppWindows.PickFolderAsync;
            control.PickConfig = AppWindows.PickConfigAsync;

            if (!settings.NoWindow)
                TrayApp.Started = settings.Browser
                    ? () => Launch(url)
                    : () => AppWindows.Show(registry, store, url);

            // Not on this thread: by the time an async command gets here it is running on a
            // thread-pool thread, and neither platform accepts that one. UiHost knows which thread
            // each wants - the process's first on macOS, a single-threaded-apartment thread of its
            // own on Windows - and returns when the loop has finished.
            UiHost.RunLoop(() => TrayApp.Run(shutdown.Token));
        }

        // Bounded: a connection that refuses to finish must not keep the process alive after the
        // user has asked it to quit.
        using var abort = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await app.StopAsync(abort.Token);

        // Started only now, with the port free and this process on its way out - two QuickRuns
        // fighting over one port is exactly what the rest of this file exists to prevent.
        if (restarting)
        {
            Output.Info("restarting into the new version");
            SingleInstance.Start(settings.Port);
        }

        return 0;
    }

    /// <summary>
    /// Starts the listener, waiting for the port if something is still letting go of it.
    /// <para>
    /// The case that needs the patience is QuickRun replacing itself: the new process is started as
    /// the old one exits, and a socket does not become free the instant its owner stops using it.
    /// Without this the new build failed to bind, printed into a console nobody has and vanished -
    /// an update that looked like QuickRun simply never coming back.
    /// </para>
    /// </summary>
    private static async Task<WebApplication?> ListenAsync(int port, WorkspaceStore store, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
        string? last = null;

        while (!ct.IsCancellationRequested)
        {
            var app = DaemonHost.Build(port, store);

            try
            {
                await app.StartAsync(ct);
                return app;
            }
            catch (Exception e) when (e is IOException or HttpRequestException)
            {
                last = e.Message;
                await app.DisposeAsync();

                if (DateTimeOffset.UtcNow >= deadline) break;
                Output.Info($"port {port} is still in use - waiting");
                try { await Task.Delay(500, ct); } catch (OperationCanceledException) { break; }
            }
        }

        Output.Error($"could not listen on port {port}: {last ?? "cancelled"}");
        return null;
    }

    /// <summary>
    /// Makes <c>quickrun://</c> point at the build that is running, when it does not already.
    /// Cheap, silent when there is nothing to do, and never fatal: a registration that cannot be
    /// written is a link that does not work, not a QuickRun that does not start.
    /// </summary>
    private static void RepairIntegration(int port)
    {
        try
        {
            if (Environment.ProcessPath is not { } executable) return;

            RepairAutostart(executable, port);
            RepairShellVerbs(executable);

            var status = SystemIntegration.Status();
            if (status is { Registered: true, Stale: false }) return;

            Output.Info(status.Registered
                ? "quickrun:// pointed at another copy - registering this one"
                : "registering quickrun:// for this copy");

            // The scheme only. Whether QuickRun starts with the machine is a separate decision the
            // user made once, and repairing a link must not quietly change it.
            var step = SystemIntegration.RegisterScheme(executable);
            if (!step.Ok) Output.Warn($"{step.What}: {step.Detail}");
        }
        catch (Exception e)
        {
            Output.Warn($"could not register quickrun://: {e.Message}");
        }
    }

    /// <summary>
    /// Keeps an autostart entry that already exists pointing at this build, and at the interface
    /// rather than the bare listener. Never switches it on: whether QuickRun starts with the machine
    /// is the user's decision, and an entry that is off stays off.
    /// </summary>
    private static void RepairAutostart(string executable, int port)
    {
        var autostart = SystemIntegration.Autostart();
        if (!autostart.Enabled) return;

        // An entry written before QuickRun had a window starts the headless listener - a QuickRun
        // running after every login with no icon anywhere to show it exists.
        var headless = autostart.Detail.Length > 0 && StartsHeadless();

        if (!autostart.Stale && !headless) return;

        var step = SystemIntegration.SetAutostart(true, executable, port);
        if (step.Ok) Output.Info("autostart now starts QuickRun with its tray icon");
        else Output.Warn($"{step.What}: {step.Detail}");
    }

    /// <summary>
    /// Puts "Run with QuickRun" in the file manager, and keeps it pointing here.
    /// <para>
    /// At startup rather than only in `quickrun install`, because a menu entry that requires knowing
    /// about a command is a menu entry nobody has. Unlike autostart this changes nothing about how
    /// the machine behaves on its own - it adds a line to a context menu, and `quickrun uninstall`
    /// takes it away again.
    /// </para>
    /// </summary>
    private static void RepairShellVerbs(string executable)
    {
        if (SystemIntegration.ShellVerbsCurrent(executable)) return;

        var step = SystemIntegration.RegisterShellVerbs(executable);

        if (step.Ok) Output.Info("added \"Run with QuickRun\" to the file manager");
        else Output.Warn($"{step.What}: {step.Detail}");
    }

    /// <summary>Whether the autostart entry on this machine starts the listener without a tray.</summary>
    private static bool StartsHeadless()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var run = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run");
                return run?.GetValue("QuickRun") is string value
                       && value.Contains(" daemon", StringComparison.Ordinal);
            }

            var file = SystemIntegration.Autostart().Detail;
            return File.Exists(file)
                   && File.ReadAllText(file).Contains(" daemon", StringComparison.Ordinal);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
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
