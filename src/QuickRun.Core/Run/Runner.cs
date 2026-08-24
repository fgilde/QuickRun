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
    Failed,
    Finished,
}

public sealed record RunEvent(RunEventKind Kind, string? Task, string Text);

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
public sealed class Runner(Action<RunEvent> onEvent) : IAsyncDisposable
{
    private const int MaxRestarts = 3;

    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<string, StringBuilder> _logs = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _ready = new();

    private RunConfig? _config;
    private RunOptions? _options;

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

        foreach (var step in Applicable(config.Setup))
        {
            var code = await RunStepAsync(step, "setup", options, linked.Token);
            if (linked.IsCancellationRequested) return new(false, "run cancelled");
            if (code != 0 && !step.ContinueOnError)
                return Fail($"setup step failed with exit code {code}: {Redact(step.Run, options)}");
        }

        foreach (var task in config.Tasks)
            _ready[task.Name] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await Task.WhenAll(config.Tasks.Select(t => RunTaskAsync(t, options, linked.Token)));

        if (linked.IsCancellationRequested) return new(false, "run cancelled");

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
            (line, isError) => Emit(isError ? RunEventKind.Error : RunEventKind.Output, phase, line), ct);
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

            var spec = new ProcessSpec(command, ResolveCwd(options.Workspace, task.Cwd, options.Context),
                EnvironmentFor(task, options));

            using var watcher = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var watching = WatchReadinessAsync(task, log, options, watcher.Token);

            var code = await CommandRunner.StreamAsync(spec, (line, isError) =>
            {
                lock (log) log.AppendLine(line);
                Emit(isError ? RunEventKind.Error : RunEventKind.Output, task.Name, line);
            }, ct);

            Emit(RunEventKind.TaskExited, task.Name, $"exited with code {code}");

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

        var ready = await Readiness.WaitAsync(task.ReadyWhen, Snapshot, options.ReadyTimeout, ct);
        if (!ready || ct.IsCancellationRequested) return;

        Complete(task.Name);
        Emit(RunEventKind.TaskReady, task.Name, "ready");

        if (OpenUrlFor(task) is { } url)
            Emit(RunEventKind.Info, task.Name, $"open {url}");
    }

    /// <summary>
    /// Core never launches a browser - it only reports the URL. Opening it is the CLI's or the
    /// desktop UI's decision.
    /// </summary>
    private static string? OpenUrlFor(TaskDef task)
    {
        if (task.OpenUrl is { } explicitUrl) return explicitUrl;
        if (!task.OpenReady) return null;

        return task.ReadyWhen switch
        {
            { Http: { } url } => url,
            { Port: { } port } => $"http://localhost:{port}",
            _ => null,
        };
    }

    private void Complete(string taskName)
    {
        if (_ready.TryGetValue(taskName, out var signal)) signal.TrySetResult();
    }

    // ---- environment and paths ---------------------------------------------

    private static IReadOnlyDictionary<string, string> EnvironmentFor(TaskDef? task, RunOptions options)
    {
        var config = task is null ? null : task.Env;
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var kv in options.ExtraEnv) merged[kv.Key] = Interpolator.Expand(kv.Value, options.Context);
        if (config is not null)
            foreach (var kv in config) merged[kv.Key] = Interpolator.Expand(kv.Value, options.Context);

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
