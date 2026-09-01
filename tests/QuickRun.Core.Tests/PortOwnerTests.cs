using System.Net;
using System.Net.Sockets;
using QuickRun.Core.Process;
using QuickRun.Core.Run;

namespace QuickRun.Core.Tests;

/// <summary>
/// Finding out who still holds a port, which is how a stop finishes what a job object cannot.
/// <para>
/// The reported failure: stopping the LX Family run left it answering on localhost:3001. `npm start`
/// spawns node between the process starting and the job object being assigned, and once npm exits
/// that node is in no job and in nobody's process tree - unreachable by anything that walks parents.
/// The port is the only handle left on it.
/// </para>
/// </summary>
public class PortOwnerTests
{
    // Real netstat output, German Windows: the state column is translated, which is why it is not
    // the thing the parser reads.
    private const string Netstat = """
          Aktive Verbindungen

          Proto  Lokale Adresse         Remoteadresse          Status           PID
          TCP    0.0.0.0:135            0.0.0.0:0              ABHÖREN          1084
          TCP    127.0.0.1:3001         0.0.0.0:0              ABHÖREN          24680
          TCP    127.0.0.1:13001        0.0.0.0:0              ABHÖREN          31337
          TCP    127.0.0.1:3001         127.0.0.1:52344        HERGESTELLT      24680
          TCP    [::]:445               [::]:0                 ABHÖREN          4
        """;

    [Fact]
    public void Netstat_names_the_listener()
    {
        Assert.Equal(24680, PortOwner.ParseNetstat(Netstat, 3001));
        Assert.Equal(1084, PortOwner.ParseNetstat(Netstat, 135));
        Assert.Equal(4, PortOwner.ParseNetstat(Netstat, 445));
    }

    [Fact]
    public void A_port_that_merely_ends_in_the_same_digits_is_a_different_port()
    {
        // 3001 must not be found inside 13001: that would kill a stranger's server.
        Assert.Equal(31337, PortOwner.ParseNetstat(Netstat, 13001));
        Assert.Null(PortOwner.ParseNetstat(Netstat, 300));
        Assert.Null(PortOwner.ParseNetstat(Netstat, 1));
    }

    [Fact]
    public void An_established_connection_is_not_a_listener()
    {
        // Only the row with no remote peer counts, so a client connected to 52344 is not reported
        // as owning it.
        Assert.Null(PortOwner.ParseNetstat(Netstat, 52344));
    }

    [Fact]
    public void Nothing_listening_is_nobody()
    {
        Assert.Null(PortOwner.ParseNetstat(Netstat, 9999));
        Assert.Null(PortOwner.ParseNetstat("", 3001));
        Assert.Null(PortOwner.ParseNetstat(null, 3001));
    }

    private const string Ss = """
        LISTEN 0      511          127.0.0.1:3001      0.0.0.0:*    users:(("node",pid=1234,fd=20))
        LISTEN 0      4096         127.0.0.1:13001     0.0.0.0:*    users:(("node",pid=9876,fd=21))
        LISTEN 0      128            0.0.0.0:22        0.0.0.0:*    users:(("sshd",pid=800,fd=3))
        """;

    [Fact]
    public void Ss_names_the_listener()
    {
        Assert.Equal(1234, PortOwner.ParseSs(Ss, 3001));
        Assert.Equal(9876, PortOwner.ParseSs(Ss, 13001));
        Assert.Equal(800, PortOwner.ParseSs(Ss, 22));
        Assert.Null(PortOwner.ParseSs(Ss, 8080));
    }

    [Fact]
    public void Lsof_prints_pids_and_nothing_else()
    {
        Assert.Equal(4242, PortOwner.ParseLsof("4242\n"));
        Assert.Equal(4242, PortOwner.ParseLsof("4242"));
        Assert.Null(PortOwner.ParseLsof(""));
        Assert.Null(PortOwner.ParseLsof("lsof: command not found"));
    }

    [Fact]
    public void Reclaiming_leaves_this_process_alone()
    {
        // A daemon that killed itself while tidying up a run would be a memorable bug.
        var killed = new List<int>();

        var what = PortOwner.Reclaim(3001, owner: _ => Environment.ProcessId, kill: killed.Add);

        Assert.Null(what);
        Assert.Empty(killed);
    }

    [Fact]
    public void Reclaiming_a_port_nobody_holds_does_nothing()
    {
        var killed = new List<int>();

        Assert.Null(PortOwner.Reclaim(3001, owner: _ => null, kill: killed.Add));
        Assert.Empty(killed);
    }

    [Fact]
    public void Reclaiming_kills_the_holder_and_says_so()
    {
        var killed = new List<int>();

        var what = PortOwner.Reclaim(3001, owner: _ => 24680, kill: killed.Add);

        Assert.Equal(new[] { 24680 }, killed);
        Assert.Contains("3001", what);
        Assert.Contains("24680", what);
    }

    /// <summary>
    /// The whole point, against the real operating system: a listener this test opens is found by
    /// its port and by nothing else about it.
    /// </summary>
    [Fact]
    public void A_real_listener_is_found_by_its_port()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var pid = PortOwner.ListeningPid(port);

        // netstat, ss and lsof are all allowed to be absent - a machine without any of them cannot
        // answer, and that is a "do not know", not a wrong answer.
        if (pid is null) return;

        Assert.Equal(Environment.ProcessId, pid);
    }

    [Fact]
    public void An_unused_port_has_no_owner()
    {
        // Bound and released, so nothing is listening on it by the time it is asked about.
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        Assert.Null(PortOwner.ListeningPid(port));
    }

    [Fact]
    public void A_capture_that_fails_is_not_an_answer()
    {
        var pid = PortOwner.ListeningPid(3001,
            capture: (_, _) => new CommandResult(9009, "", TimedOut: false));

        Assert.Null(pid);
    }
}
