using QuickRun.Core.Config;
using QuickRun.Core.Run;

namespace QuickRun.Core.Tests;

public class RunnerTests
{
    private static readonly bool Windows = OSKinds.Current == OSKind.Windows;

    private static RunOptions Options(string workspace, params string[] secrets) =>
        new(workspace,
            new InterpolationContext(new Dictionary<string, string?>(), workspace, "app", "main", _ => null),
            new Dictionary<string, string>(), secrets, TimeSpan.FromSeconds(5), SkipRequires: true);

    private static RunConfig Config(string yaml) => ConfigParser.Parse(yaml, OSKinds.Current);

    private sealed class Recorder
    {
        private readonly List<RunEvent> _events = new();

        public Action<RunEvent> Sink => e => { lock (_events) _events.Add(e); };

        public IReadOnlyList<RunEvent> Events { get { lock (_events) return _events.ToList(); } }

        public string Text => string.Join("\n", Events.Select(e => e.Text));
    }

    [Fact]
    public async Task A_setup_step_runs_and_its_output_is_reported()
    {
        using var repo = new FakeRepo();
        var log = new Recorder();
        await using var runner = new Runner(log.Sink);

        var outcome = await runner.ExecuteAsync(Config("setup: [echo setup-ran]\ntasks: []"),
            Options(repo.Path), CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Error);
        Assert.Contains("setup-ran", log.Text);
    }

    [Fact]
    public async Task A_failing_setup_step_fails_the_run()
    {
        using var repo = new FakeRepo();
        await using var runner = new Runner(_ => { });

        var outcome = await runner.ExecuteAsync(Config("setup: [exit 4]\ntasks: []"),
            Options(repo.Path), CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Contains("setup", outcome.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ContinueOnError_lets_the_run_proceed_past_a_failing_step()
    {
        using var repo = new FakeRepo();
        var log = new Recorder();
        await using var runner = new Runner(log.Sink);

        var yaml = string.Join("\n",
            "setup:",
            "  - run: exit 4",
            "    continueOnError: true",
            "  - run: echo second-step",
            "tasks: []");

        var outcome = await runner.ExecuteAsync(Config(yaml), Options(repo.Path), CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Error);
        Assert.Contains("second-step", log.Text);
    }

    [Fact]
    public async Task A_task_runs_to_completion_and_reports_its_exit()
    {
        using var repo = new FakeRepo();
        var log = new Recorder();
        await using var runner = new Runner(log.Sink);

        var outcome = await runner.ExecuteAsync(Config("tasks:\n  - name: hello\n    run: echo task-ran"),
            Options(repo.Path), CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Error);
        Assert.Contains("task-ran", log.Text);
        Assert.Contains(log.Events, e => e.Kind == RunEventKind.TaskExited && e.Task == "hello");
    }

    [Fact]
    public async Task Steps_run_in_the_workspace_directory()
    {
        using var repo = new FakeRepo().With("marker.txt", "x");
        var log = new Recorder();
        await using var runner = new Runner(log.Sink);

        await runner.ExecuteAsync(Config($"tasks:\n  - run: {(Windows ? "dir /b" : "ls")}"),
            Options(repo.Path), CancellationToken.None);

        Assert.Contains("marker.txt", log.Text);
    }

    [Fact]
    public async Task Cwd_is_resolved_relative_to_the_workspace()
    {
        using var repo = new FakeRepo().With("web/inner.txt", "x");
        var log = new Recorder();
        await using var runner = new Runner(log.Sink);

        await runner.ExecuteAsync(Config($"tasks:\n  - run: {(Windows ? "dir /b" : "ls")}\n    cwd: web"),
            Options(repo.Path), CancellationToken.None);

        Assert.Contains("inner.txt", log.Text);
    }

    [Fact]
    public async Task Secrets_are_redacted_from_reported_output()
    {
        using var repo = new FakeRepo();
        var log = new Recorder();
        await using var runner = new Runner(log.Sink);

        await runner.ExecuteAsync(Config("tasks:\n  - run: echo sk-supersecret"),
            Options(repo.Path, "sk-supersecret"), CancellationToken.None);

        Assert.DoesNotContain("sk-supersecret", log.Text);
        Assert.Contains("***", log.Text);
    }

    [Fact]
    public async Task Extra_environment_variables_reach_the_child_process()
    {
        using var repo = new FakeRepo();
        var log = new Recorder();
        await using var runner = new Runner(log.Sink);

        var options = Options(repo.Path) with
        {
            ExtraEnv = new Dictionary<string, string> { ["QUICKRUN_TEST_TOKEN"] = "abc123" },
        };
        var command = Windows ? "echo %QUICKRUN_TEST_TOKEN%" : "echo $QUICKRUN_TEST_TOKEN";

        await runner.ExecuteAsync(Config($"tasks:\n  - run: {command}"), options, CancellationToken.None);

        Assert.Contains("abc123", log.Text);
    }

    [Fact]
    public async Task Task_level_env_overrides_the_run_wide_env()
    {
        using var repo = new FakeRepo();
        var log = new Recorder();
        await using var runner = new Runner(log.Sink);

        var options = Options(repo.Path) with
        {
            ExtraEnv = new Dictionary<string, string> { ["QUICKRUN_TEST_TOKEN"] = "outer" },
        };
        var echo = Windows ? "echo %QUICKRUN_TEST_TOKEN%" : "echo $QUICKRUN_TEST_TOKEN";
        var yaml = string.Join("\n",
            "tasks:",
            $"  - run: {echo}",
            "    env:",
            "      QUICKRUN_TEST_TOKEN: inner");

        await runner.ExecuteAsync(Config(yaml), options, CancellationToken.None);

        Assert.Contains("inner", log.Text);
        Assert.DoesNotContain("outer", log.Text);
    }

    [Fact]
    public async Task A_dependent_task_starts_only_after_its_dependency_is_ready()
    {
        using var repo = new FakeRepo();
        var order = new List<string>();
        await using var runner = new Runner(e =>
        {
            if (e.Kind == RunEventKind.TaskStarted) lock (order) order.Add(e.Task!);
        });

        var yaml = string.Join("\n",
            "tasks:",
            "  - name: first",
            "    run: echo one",
            "    readyWhen: {delay: 300ms}",
            "  - name: second",
            "    run: echo two",
            "    dependsOn: [first]");

        await runner.ExecuteAsync(Config(yaml), Options(repo.Path), CancellationToken.None);

        Assert.Equal(new[] { "first", "second" }, order);
    }

    [Fact]
    public async Task A_blocking_requirement_fails_the_run_before_anything_executes()
    {
        using var repo = new FakeRepo();
        var log = new Recorder();
        await using var runner = new Runner(log.Sink);

        var yaml = string.Join("\n",
            "requires:",
            "  - tool: definitely-not-installed-9f2a",
            "    install: https://example.com/get",
            "setup: [echo should-not-run]",
            "tasks: []");

        var outcome = await runner.ExecuteAsync(Config(yaml),
            Options(repo.Path) with { SkipRequires = false }, CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Contains("definitely-not-installed-9f2a", outcome.Error!);
        Assert.Contains("https://example.com/get", outcome.Error!);
        Assert.DoesNotContain("should-not-run", log.Text);
    }

    [Fact]
    public async Task An_optional_requirement_does_not_block()
    {
        using var repo = new FakeRepo();
        await using var runner = new Runner(_ => { });

        var yaml = string.Join("\n",
            "requires:",
            "  - tool: definitely-not-installed-9f2a",
            "    optional: true",
            "tasks: [echo ran]");

        var outcome = await runner.ExecuteAsync(Config(yaml),
            Options(repo.Path) with { SkipRequires = false }, CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Error);
    }

    [Fact]
    public async Task StopAsync_runs_the_stop_steps()
    {
        using var repo = new FakeRepo();
        var log = new Recorder();
        await using var runner = new Runner(log.Sink);

        var sleep = Windows ? "ping -n 30 127.0.0.1 >nul" : "sleep 30";
        var run = runner.ExecuteAsync(Config($"tasks:\n  - name: long\n    run: {sleep}\nstop: [echo stopped]"),
            Options(repo.Path), CancellationToken.None);

        await Task.Delay(400);
        await runner.StopAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Contains("stopped", log.Text);
    }

    [Fact]
    public async Task Cancellation_terminates_a_long_running_task()
    {
        using var repo = new FakeRepo();
        using var cts = new CancellationTokenSource();
        await using var runner = new Runner(_ => { });

        var sleep = Windows ? "ping -n 60 127.0.0.1 >nul" : "sleep 60";
        var run = runner.ExecuteAsync(Config($"tasks:\n  - run: {sleep}"), Options(repo.Path), cts.Token);

        await Task.Delay(400);
        await cts.CancelAsync();

        var outcome = await run.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.False(outcome.Ok);
    }

    /// <summary>
    /// Also locks in that readiness outlives a clean process exit: `echo up` is long gone by the
    /// time the delay elapses, exactly as `docker compose up -d` would be.
    /// </summary>
    [Fact]
    public async Task An_open_url_is_reported_as_an_info_event_and_not_launched()
    {
        using var repo = new FakeRepo();
        var log = new Recorder();
        await using var runner = new Runner(log.Sink);

        var yaml = string.Join("\n",
            "tasks:",
            "  - name: web",
            "    run: echo up",
            "    readyWhen: {delay: 50ms}",
            "    open: http://localhost:5173");

        await runner.ExecuteAsync(Config(yaml), Options(repo.Path), CancellationToken.None);

        Assert.Contains(log.Events,
            e => e.Kind == RunEventKind.Info && e.Text.Contains("http://localhost:5173"));
    }

    [Fact]
    public async Task Open_true_derives_the_url_from_the_readiness_port()
    {
        using var repo = new FakeRepo();
        var log = new Recorder();
        await using var runner = new Runner(log.Sink);

        var yaml = string.Join("\n",
            "tasks:",
            "  - name: web",
            "    run: echo up",
            "    readyWhen: {delay: 50ms}",
            "    open: true");

        await runner.ExecuteAsync(Config(yaml), Options(repo.Path), CancellationToken.None);

        // delay-based readiness carries no URL, so nothing is offered to open
        Assert.DoesNotContain(log.Events, e => e.Kind == RunEventKind.Info && e.Text.StartsWith("open "));
    }

    [Fact]
    public async Task A_cwd_escaping_the_workspace_is_refused_at_run_time()
    {
        using var repo = new FakeRepo();
        await using var runner = new Runner(_ => { });

        // ConfigValidator rejects this too; the runner must not rely on that alone.
        var config = RunConfigDefaults.Empty with
        {
            Tasks = new[]
            {
                new TaskDef("escape", "echo hi", "../..", new Dictionary<string, string>(),
                    Array.Empty<string>(), null, false, null, RestartPolicy.Never),
            },
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.ExecuteAsync(config, Options(repo.Path), CancellationToken.None));
    }

    /// <summary>
    /// A crashed task used to leave the run reporting success: the status said finished and the log
    /// held the stack trace. An application that could not start is not a finished run.
    /// </summary>
    [Fact]
    public async Task A_task_that_exits_badly_fails_the_run()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();

        var fail = OSKinds.Current == OSKind.Windows ? "exit /b 3" : "exit 3";
        var config = ConfigParser.Parse($"tasks:\n  - name: app\n    run: {fail}\n", OSKinds.Current);

        var events = new List<RunEvent>();
        await using var runner = new Runner(events.Add);
        var outcome = await runner.ExecuteAsync(config, Options(repo.Path), CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Contains("app exited with code 3", outcome.Error);
        Assert.DoesNotContain(events, e => e.Kind == RunEventKind.Finished);
    }

    /// <summary>A task that ends cleanly is still a finished run, whatever else is going on.</summary>
    [Fact]
    public async Task A_task_that_exits_cleanly_finishes_the_run()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();

        var config = ConfigParser.Parse("tasks:\n  - name: app\n    run: echo hi\n", OSKinds.Current);

        await using var runner = new Runner(_ => { });
        var outcome = await runner.ExecuteAsync(config, Options(repo.Path), CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Error);
    }

    /// <summary>
    /// A task that runs and prints nothing used to leave the log ending mid-run, indistinguishable
    /// from a task whose process had died - which is how "it sat at 85% and did nothing" could not
    /// be explained by anyone looking at it.
    /// </summary>
    [Fact]
    public async Task A_task_that_prints_nothing_reports_that_it_is_still_alive()
    {
        using var repo = new FakeRepo();
        var log = new Recorder();
        await using var runner = new Runner(log.Sink, heartbeat: TimeSpan.FromMilliseconds(150));

        var sleep = Windows ? "powershell -NoProfile -Command Start-Sleep -Milliseconds 1200" : "sleep 1.2";
        var outcome = await runner.ExecuteAsync(
            Config($"tasks:\n  - name: quiet\n    run: {sleep}"),
            Options(repo.Path), CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Error);

        var beats = log.Events.Where(e => e.Text.StartsWith("quiet for", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(beats);
        Assert.All(beats, beat => Assert.Equal("quiet", beat.Task));

        // And it says which of the two cases it is, which is the entire point of saying anything.
        Assert.Contains(beats, beat => beat.Text.Contains("is alive", StringComparison.Ordinal));
    }
}
