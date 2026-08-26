using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using QuickRun.App.Daemon;
using QuickRun.App.Ui;
using QuickRun.Core;
using QuickRun.Core.Config;
using QuickRun.Core.Requires;
using QuickRun.Core.Workspace;
using Spectre.Console.Cli;

namespace QuickRun.App.Commands;

/// <summary>One thing that was checked, and what came of it.</summary>
public sealed record Finding(string What, bool Ok, string Detail, bool Fatal = true);

/// <summary>
/// Checks that this installation works, here, on this machine.
/// <para>
/// Every check stands for something that actually broke: a tray menu that ended the process on a
/// malformed icon, a UI loop that threw on macOS because it was not on the first thread, a listener
/// the extension could not reach. "It works on my machine" is not something anyone can act on; a
/// green run of this is.
/// </para>
/// </summary>
public sealed class DoctorCommand : AsyncCommand<DoctorCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--no-ui")]
        [Description("Skip the window and tray icon checks. For a machine with no desktop.")]
        public bool NoUi { get; init; }
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var findings = (await HeadlessAsync()).ToList();
        if (!settings.NoUi) findings.Add(Desktop());

        foreach (var finding in findings)
            Output.Line($"{(finding.Ok ? "ok  " : finding.Fatal ? "FAIL" : "warn")}  {finding.What}: {finding.Detail}");

        var broken = findings.Count(f => !f.Ok && f.Fatal);
        Output.Line("");

        if (broken == 0)
        {
            Output.Info("everything checked out");
            return 0;
        }

        Output.Error($"{broken} check(s) failed");
        return 1;
    }

    /// <summary>
    /// Everything that can be checked without a desktop. Separate so a test can run it: the window
    /// and tray checks need a machine with a screen, and everything else does not.
    /// </summary>
    public static async Task<IReadOnlyList<Finding>> HeadlessAsync()
    {
        var findings = new List<Finding>
        {
            Version(),
            Tools(),
            Workspace(),
            Scheme(),
            Autostart(),
            Crashes(),
        };

        findings.AddRange(await ListenerAsync());
        return findings;
    }

    private static Finding Version() =>
        new("quickrun", true,
            $"{BuildInfo.Version} at {Environment.ProcessPath ?? "an unknown path"} on {OSKinds.Current.Key()}");

    private static Finding Tools()
    {
        var git = ToolChecker.Check(new ToolRequirement("git", null, null, Optional: false));
        return new("git", git.Found,
            git.Found ? git.FoundVersion ?? "found" : "not on PATH - nothing can be checked out");
    }

    /// <summary>The workspace root has to exist and be writable, or no run can start.</summary>
    private static Finding Workspace()
    {
        var store = new WorkspaceStore();

        try
        {
            Directory.CreateDirectory(store.Root);

            var probe = Path.Combine(store.Root, $".doctor-{Guid.NewGuid():n}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);

            var free = new DriveInfo(Path.GetPathRoot(store.Root)!).AvailableFreeSpace / (1024L * 1024 * 1024);
            return new("workspace", true, $"{store.Root} is writable, {free} GB free");
        }
        catch (Exception e)
        {
            return new("workspace", false, $"{store.Root} cannot be written: {e.Message}");
        }
    }

    // Not fatal: everything works from the dashboard without it. This is only how the extension
    // starts QuickRun when it is not already running.
    private static Finding Scheme()
    {
        var status = SystemIntegration.Status();
        return new("quickrun:// scheme", status.Registered && !status.Stale, status.Detail, Fatal: false);
    }

    /// <summary>
    /// Whether this installation has died before. Cheap to ask and the first thing worth knowing:
    /// a user reporting that something "just closed" now has the reason to hand.
    /// </summary>
    private static Finding Crashes()
    {
        var newest = CrashLog.Newest();

        return newest is null
            ? new("crashes", true, "none recorded", Fatal: false)
            : new("crashes", false,
                $"{newest.Value.Count} recorded, newest {newest.Value.When:yyyy-MM-dd HH:mm} - "
                + $"{newest.Value.Summary} ({newest.Value.Path})", Fatal: false);
    }

    private static Finding Autostart()
    {
        var status = SystemIntegration.Autostart();
        return new("autostart", status.Enabled && !status.Stale, status.Detail, Fatal: false);
    }

    /// <summary>
    /// The listener, and the contract the extension depends on. Checked against a listener of its
    /// own on a free port, so the answer does not depend on one happening to be running.
    /// </summary>
    private static async Task<IReadOnlyList<Finding>> ListenerAsync()
    {
        var findings = new List<Finding> { await RunningAsync() };

        var port = FreePort();
        var app = DaemonHost.Build(port, new WorkspaceStore());

        try
        {
            await app.StartAsync();
        }
        catch (Exception e)
        {
            findings.Add(new("listener", false, $"could not listen on 127.0.0.1:{port}: {e.Message}"));
            return findings;
        }

        try
        {
            using var http = new HttpClient
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}"),
                Timeout = TimeSpan.FromSeconds(5),
            };

            var ping = await http.GetAsync("/api/ping");
            findings.Add(new("listener", ping.IsSuccessStatusCode,
                ping.IsSuccessStatusCode
                    ? $"answers on 127.0.0.1:{port}"
                    : $"answered /api/ping with {(int)ping.StatusCode}"));

            findings.Add(await AsksAsync(http, "https://github.com", refused: true));
            findings.Add(await AsksAsync(http, "chrome-extension://abcdefghijklmnopabcdefghijklmnop", refused: false));
        }
        finally
        {
            await app.StopAsync();
        }

        return findings;
    }

    /// <summary>Whether a daemon is already up where the extension looks for one.</summary>
    private static async Task<Finding> RunningAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        try
        {
            var response = await http.GetAsync($"http://127.0.0.1:{DaemonHost.DefaultPort}/api/ping");
            return new("daemon", response.IsSuccessStatusCode,
                response.IsSuccessStatusCode
                    ? $"running on port {DaemonHost.DefaultPort}"
                    : $"something answered port {DaemonHost.DefaultPort} with {(int)response.StatusCode}",
                Fatal: false);
        }
        catch (Exception)
        {
            return new("daemon", false,
                $"nothing is listening on port {DaemonHost.DefaultPort} - the extension cannot reach QuickRun "
                + "until quickrun ui or quickrun daemon is running", Fatal: false);
        }
    }

    /// <summary>
    /// The confirmation gate from the outside: a run request from an ordinary page must be refused
    /// and one from an extension must not be. A security boundary, so it is checked, not assumed.
    /// </summary>
    private static async Task<Finding> AsksAsync(HttpClient http, string origin, bool refused)
    {
        var what = refused ? "a page may not start a run" : "an extension may start a run";

        try
        {
            // A request that would fail on its own merits: what is checked is who may ask, not what
            // comes back, and an empty repository never reaches a checkout.
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/run")
            {
                Content = new StringContent("{\"repo\":\"\"}", Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("Origin", origin);

            var response = await http.SendAsync(request);
            var forbidden = response.StatusCode == HttpStatusCode.Forbidden;

            return new(what, forbidden == refused,
                forbidden ? $"{origin} is refused" : $"{origin} is accepted ({(int)response.StatusCode})");
        }
        catch (Exception e)
        {
            return new(what, false, e.Message);
        }
    }

    /// <summary>
    /// A window and a tray icon, actually created.
    /// <para>
    /// Showing a window is what loads the executable's icon, and a malformed icon file there ended
    /// the process on the first right-click of the tray menu. Creating the loop at all is what threw
    /// on macOS when it ran anywhere but the first thread. Both shipped; both are exercised here.
    /// </para>
    /// </summary>
    private static Finding Desktop()
    {
        try
        {
            var error = DesktopProbe.Run(TimeSpan.FromSeconds(30));
            return error is null
                ? new("desktop", true, "a window and a tray icon can be created")
                : new("desktop", false, error);
        }
        catch (Exception e)
        {
            return new("desktop", false, e.Message);
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
