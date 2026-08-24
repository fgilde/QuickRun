using QuickRun.Core.Config;
using QuickRun.Core.Run;

namespace QuickRun.Core.Tests;

public class ReadinessTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(600);

    [Fact]
    public async Task No_readiness_condition_is_immediately_ready()
        => Assert.True(await Readiness.WaitAsync(null, () => "", Short, CancellationToken.None));

    [Fact]
    public async Task A_delay_condition_waits_and_then_succeeds()
    {
        var readyWhen = new ReadyWhen(null, null, null, TimeSpan.FromMilliseconds(50));
        Assert.True(await Readiness.WaitAsync(readyWhen, () => "", Short, CancellationToken.None));
    }

    [Fact]
    public async Task A_port_that_opens_on_the_third_attempt_succeeds()
    {
        var attempts = 0;
        var readyWhen = new ReadyWhen(5000, null, null, null);

        var ready = await Readiness.WaitAsync(readyWhen, () => "", TimeSpan.FromSeconds(5), CancellationToken.None,
            portProbe: _ => Task.FromResult(++attempts >= 3));

        Assert.True(ready);
        Assert.True(attempts >= 3);
    }

    [Fact]
    public async Task A_port_that_never_opens_times_out()
    {
        var readyWhen = new ReadyWhen(5000, null, null, null);
        Assert.False(await Readiness.WaitAsync(readyWhen, () => "", Short, CancellationToken.None,
            portProbe: _ => Task.FromResult(false)));
    }

    [Fact]
    public async Task An_http_probe_that_succeeds_is_ready()
    {
        var readyWhen = new ReadyWhen(null, "http://localhost:1/", null, null);
        Assert.True(await Readiness.WaitAsync(readyWhen, () => "", Short, CancellationToken.None,
            httpProbe: _ => Task.FromResult(true)));
    }

    [Fact]
    public async Task A_probe_that_throws_is_treated_as_not_ready()
    {
        var readyWhen = new ReadyWhen(null, "http://localhost:1/", null, null);
        Assert.False(await Readiness.WaitAsync(readyWhen, () => "", Short, CancellationToken.None,
            httpProbe: _ => throw new HttpRequestException("refused")));
    }

    [Fact]
    public async Task A_log_pattern_that_appears_is_ready()
    {
        var log = "";
        var readyWhen = new ReadyWhen(null, null, @"Now listening on: (?<url>\S+)", null);

        var waiting = Readiness.WaitAsync(readyWhen, () => log, TimeSpan.FromSeconds(5), CancellationToken.None);
        await Task.Delay(150);
        log = "info: Now listening on: http://localhost:5000";

        Assert.True(await waiting);
    }

    [Fact]
    public async Task A_log_pattern_that_never_appears_times_out()
    {
        var readyWhen = new ReadyWhen(null, null, "never-appears", null);
        Assert.False(await Readiness.WaitAsync(readyWhen, () => "nothing here", Short, CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_stops_waiting_and_reports_not_ready()
    {
        using var cts = new CancellationTokenSource(100);
        var readyWhen = new ReadyWhen(5000, null, null, null);

        Assert.False(await Readiness.WaitAsync(readyWhen, () => "", TimeSpan.FromMinutes(1), cts.Token,
            portProbe: _ => Task.FromResult(false)));
    }

    [Fact]
    public async Task An_invalid_log_regex_reports_not_ready_instead_of_throwing()
    {
        var readyWhen = new ReadyWhen(null, null, "[unclosed", null);
        Assert.False(await Readiness.WaitAsync(readyWhen, () => "anything", Short, CancellationToken.None));
    }
}
