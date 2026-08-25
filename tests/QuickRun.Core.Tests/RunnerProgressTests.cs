using QuickRun.Core.Config;
using QuickRun.Core.Run;

namespace QuickRun.Core.Tests;

public class RunnerProgressTests
{
    private static RunOptions Options(string workspace, params string[] secrets) =>
        new(workspace,
            new InterpolationContext(new Dictionary<string, string?>(), workspace, "app", "main", _ => null),
            new Dictionary<string, string>(), secrets, TimeSpan.FromSeconds(5), SkipRequires: true);

    private static RunConfig Config(string yaml) => ConfigParser.Parse(yaml, OSKinds.Current);

    private sealed class Recorder
    {
        private readonly List<RunEvent> _events = new();

        public Action<RunEvent> Sink => e => { lock (_events) _events.Add(e); };

        public IReadOnlyList<string> Errors
        {
            get
            {
                lock (_events)
                    return _events.Where(e => e.Kind == RunEventKind.Error).Select(e => e.Text).ToList();
            }
        }

        public IReadOnlyList<RunProgress> Progress
        {
            get
            {
                lock (_events)
                    return _events.Where(e => e.Kind == RunEventKind.Progress)
                        .Select(e => e.Progress!)
                        .ToList();
            }
        }
    }

    [Fact]
    public async Task Setup_steps_report_monotonic_progress_ending_at_a_hundred()
    {
        using var repo = new FakeRepo();
        var log = new Recorder();
        await using var runner = new Runner(log.Sink);

        var yaml = string.Join("\n", "setup: [echo one, echo two, echo three]", "tasks: []");
        await runner.ExecuteAsync(Config(yaml), Options(repo.Path), CancellationToken.None);

        var percents = log.Progress.Select(p => p.Percent).ToList();

        Assert.NotEmpty(percents);
        Assert.Equal(percents.OrderBy(p => p), percents);
        Assert.Equal(100, percents[^1]);
    }

    [Fact]
    public async Task Progress_details_name_the_step_being_run()
    {
        using var repo = new FakeRepo();
        var log = new Recorder();
        await using var runner = new Runner(log.Sink);

        var yaml = string.Join("\n", "setup: [echo one, echo two]", "tasks: []");
        await runner.ExecuteAsync(Config(yaml), Options(repo.Path), CancellationToken.None);

        var details = log.Progress.Select(p => p.Detail).ToList();
        Assert.Contains(details, d => d.Contains("setup 1/2"));
        Assert.Contains(details, d => d.Contains("setup 2/2"));
    }

    [Fact]
    public async Task Setup_progress_stays_inside_its_weighted_band()
    {
        using var repo = new FakeRepo();
        var log = new Recorder();
        await using var runner = new Runner(log.Sink);

        var yaml = string.Join("\n", "setup: [echo one, echo two]", "tasks: []");
        await runner.ExecuteAsync(Config(yaml), Options(repo.Path), CancellationToken.None);

        var setup = log.Progress.Where(p => p.Phase == RunPhase.Setup).Select(p => p.Percent).ToList();
        Assert.NotEmpty(setup);
        Assert.All(setup, p => Assert.InRange(p, ProgressModel.CheckoutWeight,
            ProgressModel.CheckoutWeight + ProgressModel.SetupWeight));
    }

    /// <summary>
    /// A task that becomes ready and then exits must count once, and a task that only exits must
    /// still count - otherwise the bar either overshoots or stalls.
    /// </summary>
    [Fact]
    public async Task Each_task_counts_once_towards_progress()
    {
        using var repo = new FakeRepo();
        var log = new Recorder();
        await using var runner = new Runner(log.Sink);

        var yaml = string.Join("\n",
            "tasks:",
            "  - name: a",
            "    run: echo one",
            "    readyWhen: {delay: 50ms}",
            "  - name: b",
            "    run: echo two");

        await runner.ExecuteAsync(Config(yaml), Options(repo.Path), CancellationToken.None);

        var tasks = log.Progress.Where(p => p.Phase == RunPhase.Tasks).Select(p => p.Percent).ToList();

        Assert.Equal(tasks.OrderBy(p => p), tasks);
        Assert.All(tasks, p => Assert.InRange(p, ProgressModel.CheckoutWeight + ProgressModel.SetupWeight, 100));
        Assert.Equal(100, tasks[^1]);
    }

    [Fact]
    public async Task A_run_with_no_tasks_still_reaches_a_hundred()
    {
        using var repo = new FakeRepo();
        var log = new Recorder();
        await using var runner = new Runner(log.Sink);

        var yaml = string.Join("\n", "setup: [echo only-setup]", "tasks: []");
        await runner.ExecuteAsync(Config(yaml), Options(repo.Path), CancellationToken.None);

        Assert.Equal(100, log.Progress[^1].Percent);
    }

    [Fact]
    public async Task Secrets_are_redacted_from_progress_detail()
    {
        using var repo = new FakeRepo();
        var log = new Recorder();
        await using var runner = new Runner(log.Sink);

        var yaml = string.Join("\n", "setup: [echo sk-supersecret]", "tasks: []");
        await runner.ExecuteAsync(Config(yaml), Options(repo.Path, "sk-supersecret"), CancellationToken.None);

        var details = string.Join("\n", log.Progress.Select(p => p.Detail));
        Assert.DoesNotContain("sk-supersecret", details);
    }

    /// <summary>
    /// The symptom this fixes: an app that started and works, next to a bar parked at the end of
    /// the setup phase because its readiness check has not fired yet.
    /// </summary>
    [Fact]
    public async Task A_task_that_started_moves_the_bar_before_it_is_ready()
    {
        using var repo = new FakeRepo();
        var log = new Recorder();
        await using var runner = new Runner(log.Sink);

        var yaml = string.Join("\n",
            "tasks:",
            "  - name: app",
            "    run: echo started",
            "    readyWhen: {port: 65500}");

        var options = new RunOptions(repo.Path,
            new InterpolationContext(new Dictionary<string, string?>(), repo.Path, "app", "main", _ => null),
            new Dictionary<string, string>(), Array.Empty<string>(),
            TimeSpan.FromMilliseconds(300), SkipRequires: true);

        await runner.ExecuteAsync(Config(yaml), options, CancellationToken.None);

        var tasks = log.Progress.Where(p => p.Phase == RunPhase.Tasks).ToList();
        var start = ProgressModel.CheckoutWeight + ProgressModel.SetupWeight;

        Assert.Contains(tasks, p => p.Percent > start && p.Detail.Contains("app started"));
        Assert.Contains(tasks, p => p.Detail.Contains("port 65500"));
    }

    /// <summary>Giving up on a readiness check has to be said, or the run looks stuck.</summary>
    [Fact]
    public async Task Giving_up_on_readiness_is_reported()
    {
        using var repo = new FakeRepo();
        var log = new Recorder();
        await using var runner = new Runner(log.Sink);

        var yaml = string.Join("\n",
            "tasks:",
            "  - name: app",
            "    run: echo started",
            "    readyWhen: {port: 65501}");

        var options = new RunOptions(repo.Path,
            new InterpolationContext(new Dictionary<string, string?>(), repo.Path, "app", "main", _ => null),
            new Dictionary<string, string>(), Array.Empty<string>(),
            TimeSpan.FromMilliseconds(300), SkipRequires: true);

        await runner.ExecuteAsync(Config(yaml), options, CancellationToken.None);

        Assert.Contains(log.Errors, text => text.Contains("gave up waiting for port 65501"));
        Assert.Contains(log.Errors, text => text.Contains("still running"));
        Assert.Equal(100, log.Progress[^1].Percent);
    }

    /// <summary>
    /// Three tasks start and settle in whatever order they please, and starting is worth half a
    /// task - so the two counts cross. The number the user sees must still never go backwards.
    /// </summary>
    [Fact]
    public async Task Progress_never_goes_backwards_with_several_tasks()
    {
        using var repo = new FakeRepo();
        var log = new Recorder();
        await using var runner = new Runner(log.Sink);

        var yaml = string.Join("\n",
            "tasks:",
            "  - name: a",
            "    run: echo a",
            "  - name: b",
            "    run: echo b",
            "  - name: c",
            "    run: echo c");

        await runner.ExecuteAsync(Config(yaml), Options(repo.Path), CancellationToken.None);

        var percents = log.Progress.Select(p => p.Percent).ToList();
        Assert.Equal(percents.OrderBy(p => p), percents);
        Assert.Equal(100, percents[^1]);
    }
}
