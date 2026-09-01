using QuickRun.Core.Config;
using QuickRun.Core.Run;

namespace QuickRun.Core.Tests;

/// <summary>
/// Whose "ready" a readiness probe is actually reporting.
/// <para>
/// Reported against a real repository: a run of it printed <c>apphost ready</c> and 100% while the
/// build inside it had failed, because a process from an earlier run still held the address the
/// readiness check was watching - and still answered on it. The check was right that something was
/// listening and wrong about what. A false ready is worse than none, because it is believed.
/// </para>
/// <para>
/// Driven with a real process holding a real port. A socket opened inside the test process would
/// prove nothing: the interesting case is a stranger, and this only reproduces with one.
/// </para>
/// </summary>
public class ReadinessOwnershipTests
{
    private static readonly bool Windows = OSKinds.Current == OSKind.Windows;

    private static RunOptions Options(string workspace) =>
        new(workspace,
            new InterpolationContext(new Dictionary<string, string?>(), workspace, "app", "main", _ => null),
            new Dictionary<string, string>(), Array.Empty<string>(), TimeSpan.FromSeconds(5),
            SkipRequires: true);

    /// <summary>A command that fails at once, standing in for a build that could not run.</summary>
    private static string Fails => Windows ? "cmd /c exit 1" : "false";

    [Fact]
    public async System.Threading.Tasks.Task A_task_is_not_ready_because_a_stranger_answers_its_address()
    {
        using var repo = new FakeRepo();

        using var stranger = StartServer(out var port);
        if (stranger is null) return;   // no node here: nothing to hold the address with

        var yaml = $$"""
            tasks:
              - name: apphost
                run: {{Fails}}
                readyWhen: {http: "http://127.0.0.1:{{port}}"}
            """;

        var events = new List<RunEvent>();
        await using var runner = new Runner(e => { lock (events) events.Add(e); });

        await runner.ExecuteAsync(ConfigParser.Parse(yaml, OSKinds.Current), Options(repo.Path),
            CancellationToken.None);

        List<RunEvent> Seen() { lock (events) return events.ToList(); }

        // The whole point: the address answers, and the task is still not ready.
        Assert.DoesNotContain(Seen(), e => e.Kind == RunEventKind.TaskReady);

        // It says so, and says who is holding the address - a warning nobody can act on is noise.
        var warning = Seen().SingleOrDefault(e =>
            e.Kind == RunEventKind.Error && e.Text.Contains("already listening", StringComparison.Ordinal));

        Assert.NotNull(warning);
        Assert.Contains($"pid {stranger.Id}", warning!.Text);

        // And the run is judged by the task, which failed.
        Assert.Contains(Seen(), e => e.Kind == RunEventKind.TaskExited && e.Text.Contains("code 1"));
        Assert.Contains(Seen(), e => e.Kind == RunEventKind.Failed);

        await runner.StopAsync();

        Assert.False(stranger.HasExited, "the run ended a process it did not start");
    }

    /// <summary>
    /// With nothing on the address, everything is exactly as before.
    /// <para>
    /// The case that must not have changed - which is every ordinary run - so the fix above cannot
    /// have cost readiness in general.
    /// </para>
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task A_free_address_still_makes_a_task_ready()
    {
        using var repo = new FakeRepo();

        // Started by the run itself, on a port nothing holds yet: the ordinary case.
        var port = FreePort();

        var yaml = $$"""
            tasks:
              - name: server
                run: node -e "require('http').createServer((_,r)=>r.end('ok')).listen({{port}},'127.0.0.1')"
                readyWhen: {http: "http://127.0.0.1:{{port}}"}
            """;

        var events = new List<RunEvent>();
        await using var runner = new Runner(e => { lock (events) events.Add(e); });

        var run = runner.ExecuteAsync(ConfigParser.Parse(yaml, OSKinds.Current), Options(repo.Path),
            CancellationToken.None);

        // The task does not exit, so the run is over when readiness has been decided either way.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            lock (events)
                if (events.Any(e => e.Kind is RunEventKind.TaskReady or RunEventKind.Failed
                        or RunEventKind.TaskExited)
                    || events.Any(e => e.Kind == RunEventKind.Error && e.Text.Contains("gave up")))
                    break;

            await System.Threading.Tasks.Task.Delay(200);
        }

        List<RunEvent> Seen() { lock (events) return events.ToList(); }

        // No node here means no server to become ready, and nothing to conclude.
        if (Seen().Any(e => e.Kind == RunEventKind.TaskExited))
        {
            await runner.StopAsync();
            return;
        }

        Assert.Contains(Seen(), e => e.Kind == RunEventKind.TaskReady);
        Assert.DoesNotContain(Seen(), e =>
            e.Kind == RunEventKind.Error && e.Text.Contains("already listening", StringComparison.Ordinal));

        await runner.StopAsync();
        await run;
    }

    /// <summary>A port nothing is listening on, asked of the operating system.</summary>
    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// A process of its own answering HTTP on a loopback port, and the port it chose.
    /// <para>
    /// HTTP rather than a bare socket: the probe under test makes a request, and something that
    /// accepts and then says nothing is a hanging connection rather than an answer - which is a
    /// different case from the one being reproduced.
    /// </para>
    /// </summary>
    private static System.Diagnostics.Process? StartServer(out int port)
    {
        port = 0;

        var start = new System.Diagnostics.ProcessStartInfo("node")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        start.ArgumentList.Add("-e");
        start.ArgumentList.Add(
            "const s=require('http').createServer((_,r)=>r.end('ok'));"
            + "s.listen(0,'127.0.0.1',()=>console.log(s.address().port));"
            + "setTimeout(()=>process.exit(0),120000);");

        System.Diagnostics.Process? node;
        try { node = System.Diagnostics.Process.Start(start); }
        catch (System.ComponentModel.Win32Exception) { return null; }

        if (node is null) return null;

        var line = node.StandardOutput.ReadLine();

        if (!int.TryParse((line ?? "").Trim(), out port) || port <= 0)
        {
            try { node.Kill(entireProcessTree: true); } catch { /* already gone */ }
            node.Dispose();
            return null;
        }

        return node;
    }
}
