using System.Collections.Concurrent;
using System.Text;
using QuickRun.Core.Config;
using QuickRun.Core.Process;
using QuickRun.Core.Requires;

namespace QuickRun.Core.Run;

public enum RunEventKind
{
    Info,
    Output,
    Error,
    TaskStarted,
    TaskReady,
    TaskExited,
    Progress,
    Failed,
    Finished,

    /// <summary>Stopped on request. A run that was asked to stop did not fail, and did not finish.</summary>
    Cancelled,
}

/// <summary>
/// One thing that happened during a run. <see cref="Progress"/> is set only on
/// <see cref="RunEventKind.Progress"/> events, so consumers can render a bar and a log from the
/// same ordered stream.
/// </summary>
public sealed record RunEvent(RunEventKind Kind, string? Task, string Text, RunProgress? Progress = null);

public sealed record RunOutcome(bool Ok, string? Error);

public sealed record RunOptions(
    string Workspace,
    InterpolationContext Context,
    IReadOnlyDictionary<string, string> ExtraEnv,
    IReadOnlyList<string> Secrets,
    TimeSpan ReadyTimeout,
    bool SkipRequires = false);

/// <summary>
/// Runs a config: prerequisites, setup steps, then the tasks with their dependencies and readiness.
/// Every string handed to the caller passes through <see cref="Emit"/>, which redacts secrets -
/// that is the one invariant this class must not let anything bypass.
/// </summary>
public sealed class Runner(Action<RunEvent> onEvent, ProcessGroup? group = null) : IAsyncDisposable
{
    private const int MaxRestarts = 3;

    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<string, StringBuilder> _logs = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _ready = new();
    private readonly ConcurrentDictionary<string, bool> _settled = new();

    /// <summary>The process each task is currently running as, for the window probe and the log.</summary>
    private readonly ConcurrentDictionary<string, int> _pids = new();

    /// <summary>
    /// Everything this run starts. Given from outside so it can outlive the run: a task that
    /// launches a server and exits leaves it running, and stopping has to reach it afterwards.
    /// </summary>
    private readonly ProcessGroup? _group = group;

    private RunConfig? _config;
    private RunOptions? _options;
    private int _taskCount;
    private int _tasksSettled;
    private int _tasksStarted;
    private int _reportedPercent;
    private readonly object _progressGate = new();

    public async Task<RunOutcome> ExecuteAsync(RunConfig config, RunOptions options, CancellationToken ct)
    {
        _config = config;
        _options = options;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _stop.Token);

        if (!options.SkipRequires)
        {
            var blockers = Blockers(config);
            if (blockers.Length > 0) return Fail(string.Join("\n", blockers));
        }

        var steps = Applicable(config.Setup).ToList();
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            ReportProgress(RunPhase.Setup, ProgressModel.StepPercent(i, steps.Count),
                $"setup {i + 1}/{steps.Count}: {Redact(step.Run, options)}");

            var code = await RunStepAsync(step, "setup", options, linked.Token);
            if (linked.IsCancellationRequested) return new(false, "run cancelled");
            if (code != 0 && !step.ContinueOnError)
                return Fail($"setup step failed with exit code {code}: {Redact(step.Run, options)}");
        }
        if (steps.Count > 0) ReportProgress(RunPhase.Setup, 100, "setup complete");

        _taskCount = config.Tasks.Count;
        foreach (var task in config.Tasks)
            _ready[task.Name] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        ReportProgress(RunPhase.Tasks, 0,
            _taskCount == 0 ? "nothing to start" : $"starting {_taskCount} task(s)");

        await Task.WhenAll(config.Tasks.Select(t => RunTaskAsync(t, options, linked.Token)));

        if (linked.IsCancellationRequested) return new(false, "run cancelled");

        ReportProgress(RunPhase.Tasks, 100, "finished");
        Emit(RunEventKind.Finished, null, "all tasks finished");
        return new(true, null);
    }

    /// <summary>Cancels the running tasks, then executes the config's stop commands.</summary>
    public async Task StopAsync()
    {
        if (!_stop.IsCancellationRequested) await _stop.CancelAsync();
        if (_config is null || _options is null) return;

        foreach (var step in Applicable(_config.Stop))
            await RunStepAsync(step, "stop", _options, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_stop.IsCancellationRequested) await _stop.CancelAsync();
        _stop.Dispose();
    }

    // ---- phases -------------------------------------------------------------

    private static string[] Blockers(RunConfig config) =>
        ToolChecker.CheckAll(config.Requires)
            .Where(result => result.Blocks)
            .Select(result => result.Requirement.Install is { } install
                ? $"{result.Describe()} - install from {install}"
                : result.Describe())
            .ToArray();

    private static IEnumerable<Step> Applicable(IReadOnlyList<Step> steps)
    {
        var platform = OSKinds.Current.Key();
        return steps.Where(s => s.When.Count == 0
                                || s.When.Contains(platform, StringComparer.OrdinalIgnoreCase));
    }

    private async Task<int> RunStepAsync(Step step, string phase, RunOptions options, CancellationToken ct)
    {
        var command = Interpolator.Expand(step.Run, options.Context);
        Emit(RunEventKind.Info, phase, $"$ {command}");

        var spec = new ProcessSpec(command, ResolveCwd(options.Workspace, step.Cwd, options.Context),
            EnvironmentFor(null, options));

        return await CommandRunner.StreamAsync(spec,
            (line, isError) => Emit(isError ? RunEventKind.Error : RunEventKind.Output, phase, line),
            ct, group: _group);
    }

    private async Task RunTaskAsync(TaskDef task, RunOptions options, CancellationToken ct)
    {
        await WaitForDependenciesAsync(task, options, ct);
        if (ct.IsCancellationRequested) { Complete(task.Name); return; }

        var log = _logs.GetOrAdd(task.Name, _ => new StringBuilder());

        for (var attempt = 1; attempt <= MaxRestarts; attempt++)
        {
            var command = Interpolator.Expand(task.Run, options.Context);
            Emit(RunEventKind.TaskStarted, task.Name, $"$ {command}");
            StartedTask(task);

            var spec = new ProcessSpec(command, ResolveCwd(options.Workspace, task.Cwd, options.Context),
                EnvironmentFor(task, options));

            using var watcher = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var watching = WatchReadinessAsync(task, log, options, watcher.Token);

            // A desktop application's window is the only thing the user is waiting for, and it
            // opens behind whatever they were looking at. Web tasks announce a URL instead, and
            // the browser raises itself when that is opened, so they are left alone.
            var wantsWindow = task.ReadyWhen is { Window: true }
                              || (!task.OpenReady && task.OpenUrl is null);

            var raiseWindow = new Action<int>(pid =>
            {
                _pids[task.Name] = pid;

                // The process id, said out loud: it is what a user needs to find the thing in a task
                // manager, and what makes "it is still starting" checkable rather than a claim.
                Emit(RunEventKind.Info, task.Name, $"pid {pid}");

                if (wantsWindow) _ = Foreground.RaiseAsync(pid, watcher.Token);
            });

            var code = await CommandRunner.StreamAsync(spec, (line, isError) =>
            {
                lock (log) log.AppendLine(line);
                Emit(isError ? RunEventKind.Error : RunEventKind.Output, task.Name, line);
            }, ct, raiseWindow, _group);

            Emit(RunEventKind.TaskExited, task.Name, $"exited with code {code}");
            SettleTask(task.Name, $"{task.Name} exited with code {code}");

            // Readiness describes the service, not the process. `docker compose up -d` exits long
            // before its port opens, so a clean exit keeps the watcher running to its own timeout.
            // A failed exit cancels it - nothing is going to come up.
            if (code != 0) await watcher.CancelAsync();
            await watching;
            Complete(task.Name);

            var shouldRetry = code != 0
                              && task.Restart == RestartPolicy.OnFailure
                              && attempt < MaxRestarts
                              && !ct.IsCancellationRequested;
            if (!shouldRetry) return;

            // ponytail: fixed 1s/2s/4s backoff, three attempts; make it configurable when asked
            var backoff = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
            Emit(RunEventKind.Info, task.Name, $"restarting in {backoff.TotalSeconds:0}s (attempt {attempt + 1})");
            try { await Task.Delay(backoff, ct); } catch (OperationCanceledException) { return; }
        }
    }

    private async Task WaitForDependenciesAsync(TaskDef task, RunOptions options, CancellationToken ct)
    {
        foreach (var dependency in task.DependsOn)
        {
            if (!_ready.TryGetValue(dependency, out var signal)) continue;

            var finished = await Task.WhenAny(signal.Task, Task.Delay(options.ReadyTimeout, CancellationToken.None));
            if (finished != signal.Task)
                Emit(RunEventKind.Error, task.Name,
                    $"'{dependency}' did not become ready within {options.ReadyTimeout.TotalSeconds:0}s, starting anyway");

            if (ct.IsCancellationRequested) return;
        }
    }

    private async Task WatchReadinessAsync(TaskDef task, StringBuilder log, RunOptions options, CancellationToken ct)
    {
        string Snapshot() { lock (log) return log.ToString(); }

        var ready = await Readiness.WaitAsync(task.ReadyWhen, Snapshot, options.ReadyTimeout, ct,
            windowProbe: () => _pids.TryGetValue(task.Name, out var pid) && Foreground.HasWindow(pid));

        if (!ready && !ct.IsCancellationRequested)
        {
            // The task is still running: it just never met its condition. Saying nothing leaves a
            // progress bar frozen next to an application that works, which is what this fixes.
            Emit(RunEventKind.Error, task.Name,
                $"gave up waiting for {Describe(task.ReadyWhen)} after {options.ReadyTimeout.TotalSeconds:0}s"
                + " - the task is still running, so it may only be slow");
            SettleTask(task.Name, $"{task.Name} started, readiness not confirmed");
            Complete(task.Name);
            return;
        }

        if (!ready || ct.IsCancellationRequested) return;

        Complete(task.Name);
        Emit(RunEventKind.TaskReady, task.Name, "ready");
        SettleTask(task.Name, $"{task.Name} ready");

        if (OpenUrlFor(task, Snapshot) is { } url)
            Emit(RunEventKind.Info, task.Name, $"open {url}");
    }

    /// <summary>
    /// Core never launches a browser - it only reports the URL. Opening it is the CLI's or the
    /// desktop UI's decision.
    /// </summary>
    /// <param name="log">
    /// The task's output. A task that becomes ready on a log pattern has no declared address, and
    /// the pattern was matched against the line that almost always contains one - so that line is
    /// where the address comes from. Loopback only: a build log is full of other links.
    /// </param>
    private static string? OpenUrlFor(TaskDef task, Func<string> log)
    {
        if (task.OpenUrl is { } explicitUrl) return explicitUrl;
        if (!task.OpenReady) return null;

        return task.ReadyWhen switch
        {
            { Http: { } url } => url,
            { Port: { } port } => $"http://localhost:{port}",
            { Log: not null } => LocalAddress.Last(log()),
            _ => null,
        };
    }

    /// <summary>
    /// Counts a task as settled exactly once, whether it became ready or merely exited - a task
    /// that does both must not count twice, and one that only exits must still count.
    /// </summary>
    private void SettleTask(string taskName, string detail)
    {
        if (!_settled.TryAdd(taskName, true)) return;

        var settled = Interlocked.Increment(ref _tasksSettled);
        ReportProgress(RunPhase.Tasks, ProgressModel.StepPercent(settled, _taskCount), detail);
    }

    /// <summary>
    /// Half the credit for launching, the other half for becoming ready. A long-running task never
    /// finishes, so waiting for readiness to move the bar at all leaves it parked through the whole
    /// startup - and a bar that does not move reads as "stuck", not as "starting".
    /// </summary>
    private void StartedTask(TaskDef task)
    {
        if (_settled.ContainsKey(task.Name)) return;

        var started = Interlocked.Increment(ref _tasksStarted);
        var percent = ProgressModel.StepPercent(started, Math.Max(1, _taskCount * 2));

        ReportProgress(RunPhase.Tasks, percent,
            task.ReadyWhen is null
                ? $"{task.Name} started"
                : $"{task.Name} started, waiting for {Describe(task.ReadyWhen)}");
    }

    /// <summary>What a task is waiting for, in the words the config used.</summary>
    private static string Describe(ReadyWhen? readyWhen) => readyWhen switch
    {
        { Port: { } port } => $"port {port}",
        { Http: { } url } => url,
        { Window: true } => "its window",
        { Log: { } pattern } => $"'{pattern}' in the output",
        { Delay: { } delay } => $"{delay.TotalSeconds:0}s",
        _ => "the process to start",
    };

    /// <summary>
    /// Reports progress, never backwards. Tasks start and settle in whatever order they please, and
    /// half-credit for starting means the two counts can cross - a bar that jumps back is worse
    /// than one that is coarse.
    /// </summary>
    private void ReportProgress(RunPhase phase, int phasePercent, string detail)
    {
        var total = ProgressModel.Total(phase, phasePercent);

        lock (_progressGate)
        {
            total = Math.Max(total, _reportedPercent);
            _reportedPercent = total;
        }

        onEvent(new RunEvent(RunEventKind.Progress, null, detail,
            new RunProgress(phase, total, Redact(detail, _options))));
    }

    private void Complete(string taskName)
    {
        if (_ready.TryGetValue(taskName, out var signal)) signal.TrySetResult();
    }

    // ---- environment and paths ---------------------------------------------

    /// <summary>
    /// The environment a command runs with, from general to specific: what QuickRun sets, then the
    /// config's own <c>env</c> block, then the values of inputs that name an environment variable,
    /// then the task's own <c>env</c>. Later wins.
    /// </summary>
    private IReadOnlyDictionary<string, string> EnvironmentFor(TaskDef? task, RunOptions options)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // MSBuild's worker nodes outlive the build that started them and keep its output pipe
            // open, which used to look like a run frozen after `dotnet restore`. A config that wants
            // them can set this back, because the config's own env is applied after this.
            ["MSBUILDDISABLENODEREUSE"] = "1",
        };

        if (_config is { Env.Count: > 0 } config)
            foreach (var kv in config.Env) merged[kv.Key] = Interpolator.Expand(kv.Value, options.Context);

        foreach (var kv in options.ExtraEnv) merged[kv.Key] = Interpolator.Expand(kv.Value, options.Context);

        if (task is not null)
            foreach (var kv in task.Env) merged[kv.Key] = Interpolator.Expand(kv.Value, options.Context);

        return merged;
    }

    /// <summary>Defence in depth behind ConfigValidator: a cwd must never leave the workspace.</summary>
    private static string ResolveCwd(string workspace, string? cwd, InterpolationContext ctx)
    {
        var relative = cwd is null ? "." : Interpolator.Expand(cwd, ctx);
        var resolved = Path.GetFullPath(Path.Combine(workspace, relative));
        var root = Path.GetFullPath(workspace);

        if (!resolved.StartsWith(root, StringComparison.Ordinal))
            throw new InvalidOperationException($"cwd '{cwd}' points outside the workspace");

        return resolved;
    }

    // ---- reporting ----------------------------------------------------------

    private void Emit(RunEventKind kind, string? task, string text) =>
        onEvent(new RunEvent(kind, task, Redact(text, _options)));

    private static string Redact(string text, RunOptions? options) =>
        options is null ? text : Interpolator.Redact(text, options.Secrets);

    private RunOutcome Fail(string message)
    {
        Emit(RunEventKind.Failed, null, message);
        return new(false, message);
    }
}
