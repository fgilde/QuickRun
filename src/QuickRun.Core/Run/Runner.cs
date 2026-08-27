using System.Collections.Concurrent;
using System.Text;
using QuickRun.Core.Config;
using QuickRun.Core.Process;
using QuickRun.Core.Requires;
using SysProcess = System.Diagnostics.Process;

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
    bool SkipRequires = false,
    /// <summary>
    /// Where QuickRun may install a missing tool. Null means it may not - the CLI's
    /// <c>--no-install</c>, and every test that has no business downloading a runtime.
    /// </summary>
    string? ToolRoot = null);

/// <summary>
/// Runs a config: prerequisites, setup steps, then the tasks with their dependencies and readiness.
/// Every string handed to the caller passes through <see cref="Emit"/>, which redacts secrets -
/// that is the one invariant this class must not let anything bypass.
/// </summary>
/// <param name="heartbeat">
/// How long a task may say nothing before the log reports on it. Injectable so a test need not
/// wait half a minute for the thing it is testing.
/// </param>
public sealed class Runner(Action<RunEvent> onEvent, ProcessGroup? group = null, TimeSpan? heartbeat = null)
    : IAsyncDisposable
{
    private const int MaxRestarts = 3;

    /// <summary>How long a task may say nothing before the log asks whether it is still there.</summary>
    private readonly TimeSpan _heartbeat = heartbeat ?? TimeSpan.FromSeconds(30);

    /// <summary>A pre-flight courtesy gets this long and not a second more.</summary>
    private static readonly TimeSpan PreflightBudget = TimeSpan.FromSeconds(2);

    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<string, StringBuilder> _logs = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _ready = new();
    private readonly ConcurrentDictionary<string, bool> _settled = new();

    /// <summary>The process each task is currently running as, for the window probe and the log.</summary>
    private readonly ConcurrentDictionary<string, int> _pids = new();

    /// <summary>Directories of tools installed for this run, to go in front of its PATH.</summary>
    private readonly ConcurrentBag<string> _toolPaths = new();

    /// <summary>
    /// Tasks that ended badly, with the exit code they ended on.
    /// <para>
    /// A crashed task used to leave the run reporting success: the log had the stack trace and the
    /// status said finished. An application that could not bind its port is not a finished run.
    /// </para>
    /// </summary>
    private readonly ConcurrentDictionary<string, int> _failures = new();

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
            var blockers = await EnsureRequirementsAsync(config, options, linked.Token);
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

        // A task that exited badly is a failed run, whatever the others did. Saying "finished"
        // because every task got as far as exiting is how a crash looked like a success.
        if (!_failures.IsEmpty)
        {
            var failed = _failures.OrderBy(f => f.Key, StringComparer.Ordinal)
                .Select(f => $"{f.Key} exited with code {f.Value}");
            return Fail(string.Join("; ", failed));
        }

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

    /// <summary>
    /// Checks what the config requires and installs what is missing, where that can be done.
    /// Returns what is still missing afterwards - which is what stops the run.
    /// <para>
    /// "Install .NET 10 first" is setup documentation, and a tool whose whole promise is that you
    /// do not have to read the setup documentation should not end at one. So a missing toolchain
    /// QuickRun knows how to fetch is fetched into its own folder and put in front of this run's
    /// PATH only - the machine is left exactly as it was.
    /// </para>
    /// </summary>
    private async Task<string[]> EnsureRequirementsAsync(
        RunConfig config, RunOptions options, CancellationToken ct)
    {
        var blockers = new List<string>();

        foreach (var check in ToolChecker.CheckAll(config.Requires))
        {
            if (!check.Blocks) continue;

            if (options.ToolRoot is { } root && Provisioner.Handles(check.Requirement.Tool))
            {
                ReportProgress(RunPhase.Setup, 0, $"installing {check.Requirement.Tool}");

                // What has been installed already goes along: pnpm is installed by npm, and a Node
                // provisioned a moment ago is not on the machine's PATH.
                var directories = await Provisioner
                    .EnsureAsync(check.Requirement, root, _toolPaths.ToList(),
                        line => Emit(RunEventKind.Info, "requires", line), ct)
                    .ConfigureAwait(false);

                if (directories is not null)
                {
                    foreach (var directory in directories) _toolPaths.Add(directory);
                    continue;
                }
            }

            blockers.Add(check.Requirement.Install is { } install
                ? $"{check.Describe()} - install from {install}"
                : check.Describe());
        }

        return blockers.ToArray();
    }

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

    private async Task RunTaskAsync(TaskDef declared, RunOptions options, CancellationToken ct)
    {
        // Readiness and the address to open are written by the same hand as the command and often
        // point at the same input: a config that starts on ${inputs.port} has to be able to wait
        // for that port too. Expanded once, here, so everything below sees resolved values.
        var task = Resolved(declared, options);

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

            // Something already listening where this task is about to listen means readiness will
            // pass on a stranger: the address answers before the task has started. Worth saying out
            // loud, because the usual reason is an earlier run of the same repository still running -
            // and the task itself is then about to fail to bind.
            if (attempt == 1) await WarnIfTakenAsync(task, options, ct);

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

            // A task that runs and says nothing looks exactly like a task that has died, and that
            // ambiguity is how "it sat at 85% for ever and did nothing" stayed unexplainable. So it
            // no longer stays quiet: while there is no output, the log says whether the process is
            // still there, which tells the two apart without anyone having to open a task manager.
            var lastOutput = DateTimeOffset.UtcNow.Ticks;
            using var quiet = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var beating = HeartbeatAsync(task, () => Interlocked.Read(ref lastOutput), quiet.Token);

            var code = await CommandRunner.StreamAsync(spec, (line, isError) =>
            {
                Interlocked.Exchange(ref lastOutput, DateTimeOffset.UtcNow.Ticks);
                lock (log) log.AppendLine(line);
                Emit(isError ? RunEventKind.Error : RunEventKind.Output, task.Name, line);
            }, ct, raiseWindow, _group);

            await quiet.CancelAsync();
            await beating;

            Emit(RunEventKind.TaskExited, task.Name, $"exited with code {code}");
            SettleTask(task.Name, $"{task.Name} exited with code {code}");

            // Not while stopping: a task killed on request exits non-zero because it was killed.
            if (code != 0 && !ct.IsCancellationRequested) _failures[task.Name] = code;
            else _failures.TryRemove(task.Name, out _);

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

    /// <summary>
    /// Reports a silent task, and whether its process still exists.
    /// <para>
    /// A long build prints nothing for minutes; so does a task whose process never started at all.
    /// Without this the log ends mid-run in both cases and there is nothing to tell them apart.
    /// </para>
    /// </summary>
    private async Task HeartbeatAsync(TaskDef task, Func<long> lastOutput, CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(_heartbeat);

            while (await timer.WaitForNextTickAsync(ct))
            {
                var silence = DateTimeOffset.UtcNow - new DateTimeOffset(lastOutput(), TimeSpan.Zero);
                if (silence < _heartbeat) continue;

                var pid = _pids.TryGetValue(task.Name, out var id) ? id : (int?)null;

                var process = pid is null
                    ? "no process yet - it has not started"
                    : Alive(pid.Value) switch
                    {
                        true => $"pid {pid} is alive",
                        false => $"pid {pid} is gone",
                        null => $"pid {pid}, cannot tell",
                    };

                Emit(RunEventKind.Info, task.Name,
                    $"quiet for {silence.TotalSeconds:0}s: {process}"
                    + (task.ReadyWhen is null ? "" : $", waiting for {Describe(task.ReadyWhen)}"));
            }
        }
        catch (OperationCanceledException)
        {
            // the task finished, which is the normal way out of here
        }
    }

    /// <summary>Whether a pid is still running. Null when the question cannot be answered.</summary>
    private static bool? Alive(int pid)
    {
        try { using var process = SysProcess.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
        catch { return null; }
    }

    /// <summary>
    /// Says so when the address a task waits for is already answering before the task starts.
    /// Readiness cannot tell two servers apart, so this is the only chance to notice.
    /// </summary>
    private async Task WarnIfTakenAsync(TaskDef task, RunOptions options, CancellationToken ct)
    {
        var readyWhen = task.ReadyWhen;
        if (readyWhen is null) return;

        try
        {
            // On a budget, because this runs before the task starts: a probe that takes its time -
            // a proxy that swallows loopback, a name that does not resolve - would delay the very
            // thing it is trying to warn about, and a courtesy must never be able to do that.
            var probe = readyWhen switch
            {
                { Port: { } port } => Readiness.PortOpenAsync(port),
                { Http: { } url } => Readiness.HttpAnsweringAsync(Interpolator.Expand(url, options.Context)),
                _ => Task.FromResult(false),
            };

            var taken = await probe.WaitAsync(PreflightBudget, ct);

            if (!taken) return;

            var what = readyWhen.Port is { } number ? $"port {number}" : readyWhen.Http;
            Emit(RunEventKind.Error, task.Name,
                $"something is already listening on {what} before this task started - readiness cannot "
                + "tell it apart from this run, and this task will probably fail to bind. An earlier "
                + "run that is still going is the usual reason.");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // A pre-flight courtesy, never a reason to fail a run.
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

    /// <summary>The task with its readiness condition and open address expanded.</summary>
    private static TaskDef Resolved(TaskDef task, RunOptions options)
    {
        string? Expand(string? value) =>
            value is null ? null : Interpolator.Expand(value, options.Context);

        return task with
        {
            ReadyWhen = task.ReadyWhen is { } ready
                ? ready with { Http = Expand(ready.Http), Log = Expand(ready.Log) }
                : null,
            OpenUrl = Expand(task.OpenUrl),
        };
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

        await WarnIfAnsweringNotFoundAsync(task, options, ct);

        if (OpenUrlFor(task, Snapshot) is { } url)
            Emit(RunEventKind.Info, task.Name, $"open {url}");
    }

    /// <summary>
    /// Says so when the address is answering, but answering "not found".
    /// <para>
    /// Anything below 500 counts as ready on purpose: a dev server whose root path is not routed is
    /// still up, and waiting for a 200 would hang on it for ever. But a web project built in the
    /// wrong configuration answers 404 for its entire front end - the server is up, the application
    /// is not there - and the run then said ready and opened a browser on an empty page. "It ran,
    /// but no interface came up", in two different repositories, for exactly this reason.
    /// </para>
    /// </summary>
    private async Task WarnIfAnsweringNotFoundAsync(TaskDef task, RunOptions options, CancellationToken ct)
    {
        if (task.ReadyWhen is not { Http: { } address }) return;

        var url = Interpolator.Expand(address, options.Context);
        var status = await Readiness.HttpStatusAsync(url).WaitAsync(PreflightBudget, ct);

        if (status is not >= 400) return;

        Emit(RunEventKind.Error, task.Name,
            $"{url} answers {status}, which counts as ready but is not a running application - the "
            + "server is up and what should be at that address is not. A front end that is only "
            + "built in Release, or a path that is not routed, both look like this.");
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

        // A toolchain QuickRun installed for this run goes in front of the PATH of this run's
        // processes, and nowhere else: the machine's own PATH is never written to, so a tool the
        // machine already has keeps winning everywhere except here.
        if (!_toolPaths.IsEmpty)
        {
            var inherited = merged.GetValueOrDefault("PATH")
                            ?? Environment.GetEnvironmentVariable("PATH");

            foreach (var kv in Provisioner.EnvironmentFor(_toolPaths.ToList(), inherited))
                merged[kv.Key] = kv.Value;
        }

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
