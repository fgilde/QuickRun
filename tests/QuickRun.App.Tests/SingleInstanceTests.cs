using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using QuickRun.App.Daemon;
using QuickRun.Core.Workspace;

namespace QuickRun.App.Tests;

/// <summary>
/// One QuickRun per machine, and a second start that is useful rather than fatal.
/// <para>
/// Every quickrun:// link, every double-click and every autostart entry is another attempt to
/// start one. The ones that lost the race used to either fail on a bind error or - worse - become
/// a second listener nobody could see, with runs spread across instances.
/// </para>
/// </summary>
public class SingleInstanceTests : IAsyncLifetime
{
    private readonly int _port = FreePort();
    private WebApplication? _app;
    private string? _home;
    private DaemonHost.HostControl _control = new();

    public async Task InitializeAsync()
    {
        _home = Directory.CreateTempSubdirectory("quickrun-instance-").FullName;
        _app = DaemonHost.Build(_port, new WorkspaceStore(_home));
        _control = _app.Services.GetRequiredService<DaemonHost.HostControl>();
        await _app.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_app is not null) await _app.StopAsync();
        if (_home is not null) try { Directory.Delete(_home, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task A_running_instance_is_found()
    {
        Assert.True(await SingleInstance.RunningAsync(_port));
    }

    [Fact]
    public async Task A_port_nobody_listens_on_is_not_one()
    {
        Assert.False(await SingleInstance.RunningAsync(FreePort()));
    }

    /// <summary>
    /// The point of the whole thing: a second start hands over what it came for. Without a window
    /// to raise the answer is "no" rather than a lie, so the caller can open the page instead.
    /// </summary>
    [Fact]
    public async Task Asking_it_to_show_itself_reaches_the_window()
    {
        string? shown = null;
        _control.ShowWindow = hash => shown = hash;

        Assert.True(await SingleInstance.ShowAsync(_port));
        Assert.Equal("", shown);
    }

    [Fact]
    public async Task A_link_carries_its_repository_to_the_window()
    {
        string? shown = null;
        _control.ShowWindow = hash => shown = hash;

        Assert.True(await SingleInstance.ShowAsync(_port, "repo=fgilde%2FQuickRun&ref=main"));
        Assert.Equal("#run?repo=fgilde%2FQuickRun&ref=main", shown);
    }

    /// <summary>
    /// A headless QuickRun has no window, and saying it showed one would leave the caller waiting
    /// for something that is never going to appear instead of opening the page.
    /// </summary>
    [Fact]
    public async Task Without_a_window_it_says_so()
    {
        _control.ShowWindow = null;

        Assert.False(await SingleInstance.ShowAsync(_port));
    }

    /// <summary>Nothing there is not an error, it is the normal "start one then" case.</summary>
    [Fact]
    public async Task Showing_a_QuickRun_that_is_not_there_fails_quietly()
    {
        Assert.False(await SingleInstance.ShowAsync(FreePort()));
    }

    private static int FreePort()
    {
        using var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return port;
    }
}
