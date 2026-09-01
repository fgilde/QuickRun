using System.Net;
using System.Net.Sockets;
using QuickRun.Core.Config;
using QuickRun.Core.Run;

namespace QuickRun.Core.Tests;

/// <summary>
/// Closing the ports a run opened, and never anybody else's.
/// <para>
/// A stop that kills whatever holds a port would fix the reported leftover and introduce a far worse
/// bug: someone's own server, on the port a config happens to name, ended by a QuickRun run they
/// started next to it. So a port only counts as this run's when it was free before the task that
/// wanted it started, and that is what these tests hold in place.
/// </para>
/// </summary>
public class ReclaimPortsTests
{
    private static readonly bool Windows = OSKinds.Current == OSKind.Windows;

    private static RunOptions Options(string workspace, Dictionary<string, string?>? inputs = null) =>
        new(workspace,
            new InterpolationContext(inputs ?? new Dictionary<string, string?>(), workspace, "app", "main", _ => null),
            new Dictionary<string, string>(), Array.Empty<string>(), TimeSpan.FromSeconds(5), SkipRequires: true);

    private static TaskDef Task(string? http = null, int? port = null, string? openUrl = null) =>
        new("t", "echo hi", null,
            new Dictionary<string, string>(), Array.Empty<string>(),
            port is null && http is null ? null : new ReadyWhen(port, http, null, null),
            OpenReady: false, openUrl, RestartPolicy.Never);

    [Fact]
    public void A_declared_port_is_the_port()
    {
        Assert.Equal(3001, Runner.PortOf(Task(port: 3001), Options("/tmp")));
    }

    [Fact]
    public void A_readiness_url_carries_its_port()
    {
        Assert.Equal(3001, Runner.PortOf(Task(http: "http://localhost:3001"), Options("/tmp")));
        Assert.Equal(8080, Runner.PortOf(Task(http: "http://127.0.0.1:8080/health"), Options("/tmp")));
    }

    [Fact]
    public void An_interpolated_port_is_resolved_first()
    {
        // LX Family asks for its port as an input, so the URL is only a port once expanded.
        var options = Options("/tmp", new Dictionary<string, string?> { ["port"] = "4444" });

        Assert.Equal(4444, Runner.PortOf(Task(http: "http://localhost:${inputs.port}"), options));
    }

    [Fact]
    public void The_url_it_opens_counts_too()
    {
        Assert.Equal(5173, Runner.PortOf(Task(openUrl: "http://localhost:5173"), Options("/tmp")));
    }

    [Fact]
    public void A_port_somewhere_else_is_not_ours_to_close()
    {
        // Readiness against another machine says nothing about a process here, and killing whatever
        // answers on this machine's 443 would be an outstanding way to ruin someone's afternoon.
        Assert.Null(Runner.PortOf(Task(http: "https://example.com:443/health"), Options("/tmp")));
        Assert.Null(Runner.PortOf(Task(http: "http://10.0.0.5:3001"), Options("/tmp")));
    }

    [Fact]
    public void A_task_that_says_nothing_about_a_port_has_none()
    {
        Assert.Null(Runner.PortOf(Task(), Options("/tmp")));
        Assert.Null(Runner.PortOf(Task(http: "not a url"), Options("/tmp")));
    }

    /// <summary>
    /// The guarantee, against a real listener: a port that was already busy when the task started
    /// belongs to someone else, and a stop must leave it exactly as it found it.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task A_port_that_was_already_busy_survives_the_stop()
    {
        using var repo = new FakeRepo();

        // A separate process, not a listener in this one: Reclaim refuses to kill its own process
        // anyway, so a socket opened here would pass this test no matter how wrong the rule was.
        using var stranger = StartStranger(out var port);
        if (stranger is null) return;   // no node on this machine: nothing to hold a port with

        // Two dollars, because the YAML has braces of its own.
        var yaml = $$"""
            tasks:
              - name: mine
                run: {{(Windows ? "cmd /c exit 0" : "true")}}
                readyWhen: {http: "http://127.0.0.1:{{port}}"}
            """;

        await using var runner = new Runner(_ => { });
        await runner.ExecuteAsync(ConfigParser.Parse(yaml, OSKinds.Current), Options(repo.Path),
            CancellationToken.None);

        // The stop is where reclaiming happens, so the danger is here and not before.
        await runner.StopAsync();

        Assert.False(stranger.HasExited,
            $"the stop killed the process holding port {port}, which this run did not open");

        // And it still accepts, so it was not merely left as a zombie.
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        Assert.True(client.Connected, $"the stop closed port {port}, which it did not open");
    }

    /// <summary>
    /// A process of its own holding a loopback port, and the port it chose. Null when there is no
    /// node to do it with - a machine without one cannot run this check, which is not a failure.
    /// </summary>
    private static System.Diagnostics.Process? StartStranger(out int port)
    {
        port = 0;

        var start = new System.Diagnostics.ProcessStartInfo("node")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // Port 0 lets the operating system choose, and the port is printed so there is no race
        // between finding a free one and something else taking it.
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add(
            "const s=require('net').createServer();"
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
