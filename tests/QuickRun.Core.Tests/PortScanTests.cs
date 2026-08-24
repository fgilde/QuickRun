using System.Net;
using System.Net.Sockets;
using QuickRun.Core.Config;
using QuickRun.Core.Run;

namespace QuickRun.Core.Tests;

public class PortScanTests
{
    private static RunConfig Config(string yaml) => ConfigParser.Parse(yaml, OSKind.Linux);

    [Fact]
    public void No_declared_ports_means_no_conflicts()
        => Assert.Empty(PortScan.Occupied(Config("run: ./a"), _ => true));

    [Fact]
    public void A_declared_port_that_is_free_is_not_a_conflict()
        => Assert.Empty(PortScan.Occupied(
            Config("tasks:\n  - name: api\n    run: a\n    readyWhen: {port: 5000}"), _ => false));

    [Fact]
    public void A_declared_port_that_is_taken_is_reported_with_its_task()
    {
        var conflict = Assert.Single(PortScan.Occupied(
            Config("tasks:\n  - name: api\n    run: a\n    readyWhen: {port: 5000}"), _ => true));

        Assert.Equal("api", conflict.Task);
        Assert.Equal(5000, conflict.Port);
    }

    [Fact]
    public void Only_the_taken_ports_are_reported()
    {
        var yaml = string.Join("\n",
            "tasks:",
            "  - name: free",
            "    run: a",
            "    readyWhen: {port: 5000}",
            "  - name: taken",
            "    run: b",
            "    readyWhen: {port: 5001}");

        Assert.Equal("taken", Assert.Single(PortScan.Occupied(Config(yaml), p => p == 5001)).Task);
    }

    [Fact]
    public void The_real_probe_finds_a_port_this_test_is_listening_on()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            Assert.Single(PortScan.Occupied(
                Config($"tasks:\n  - name: api\n    run: a\n    readyWhen: {{port: {port}}}")));
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public void The_real_probe_reports_nothing_for_a_closed_port()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        Assert.Empty(PortScan.Occupied(
            Config($"tasks:\n  - name: api\n    run: a\n    readyWhen: {{port: {port}}}")));
    }
}
