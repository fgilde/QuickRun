using System.Collections.Concurrent;
using System.Threading.Channels;
using QuickRun.App.Commands;
using QuickRun.Core.Config;
using QuickRun.Core.Git;
using QuickRun.Core.Inputs;
using QuickRun.Core.Process;
using QuickRun.Core.Requires;
using QuickRun.Core.Run;
using QuickRun.Core.Workspace;

namespace QuickRun.App.Daemon;

public enum RunState
{
    /// <summary>The config declares inputs whose values nobody has supplied yet.</summary>
    AwaitingInput,

    /// <summary>Prepared and waiting for someone to approve the command list.</summary>
    AwaitingConfirmation,
    Running,

    /// <summary>Asked to stop, and the processes are being taken down.</summary>
    Stopping,

    Succeeded,
    Failed,
    Cancelled,
}

/// <param name="State">
/// <c>starting</c> until it reports ready, <c>ready</c> once its readiness check passed, and
/// <c>exited</c> when the process is gone. A task with no readiness check goes straight from
/// starting to exited, which is the honest answer for something that only had to run once.
/// </param>
/// <param name="Url">Where this task said it is listening, when it said anything.</param>
/// <param name="Pid">
/// The process it is running as. For a desktop application that is the only handle anyone has on
/// it - there is no address to open and nothing to probe, but there is a process to find.
/// </param>
public sealed record RunTaskStatus(string Name, string State, string? Url, int? Pid = null);

public sealed record RunSummary(
    string Id,
    string Repo,
    string Ref,
    string? Commit,
    string DisplayName,
    RunState State,
    IReadOnlyList<PlannedCommand> Commands,
    string Fingerprint,
    RunProgress? Progress,
    string? Error,
    string? Workspace,
    string? Url,
    int LiveTasks,
    /// <summary>What the config says this repository is, when it says anything.</summary>
    string? Description = null,
    /// <summary>The form the config declares, so a window can ask for the values it needs.</summary>
    IReadOnlyList<InputDef>? Inputs = null,
    /// <summary>
    /// What those inputs currently hold. A secret is listed with a null value: the form has to know
    /// the field exists, and the value must not travel back out of the process that holds it.
    /// </summary>
    IReadOnlyDictionary<string, string?>? Values = null,
    /// <summary>Every task of the run, in the order the config declares them.</summary>
    IReadOnlyList<RunTaskStatus>? Tasks = null,
    /// <summary>
    /// Processes of this run that are still alive - including ones a task left behind when it
    /// exited. A finished run with leftovers is exactly the case where "stopped" was a lie, so it is
    /// reported rather than assumed away, and stopping stays on offer.
    /// </summary>
    int Leftovers = 0,
    /// <summary>
    /// Where these commands came from: the repository's quickrun.yml, a config of your own, another
    /// launcher's scripts, or QuickRun reading the repository. Approving a plan means trusting it,
    /// and how much trust it deserves depends on who wrote it.
    /// </summary>
    string Origin = "repository",
    /// <summary>
    /// Tools this run needs, the machine does not have, and QuickRun would install before starting
    /// - said before anyone approves anything, because installing a runtime is a change to the
    /// machine and nobody should find out about it from the log.
    /// </summary>
    IReadOnlyList<string>? Provisions = null);

/// <summary>
/// Tracks the runs the listener has been asked for.
/// <para>
/// Preparation and execution are deliberately separate: <see cref="PrepareAsync"/> checks the
/// repository out and builds the plan but executes nothing, and <see cref="Confirm"/> is what
/// starts it. That keeps the confirmation gate in place even though the browser, not a desktop
/// dialog, is what shows the commands.
/// </para>
/// </summary>
/// <param name="openUrl">
/// How to open the address a run reports. Core never opens anything itself, and the daemon is the
/// one place that may: a run started from the browser is expected to end up in the browser.
/// </param>
public sealed class RunRegistry(WorkspaceStore store, Action<string>? openUrl = null)
{
    private const int ReplayBufferSize = 500;

    /// <summary>How long a config's stop commands get before the run is finalised anyway.</summary>
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, Entry> _runs = new(StringComparer.Ordinal);

    public bool AnyActive => _runs.Values.Any(e =>
        e.Summary.State is RunState.Running or RunState.Stopping
            or RunState.AwaitingConfirmation or RunState.AwaitingInput);

    public IReadOnlyList<RunSummary> All() => _runs.Values.Select(Reported).ToList();

    public RunSummary? Get(string id) => _runs.TryGetValue(id, out var entry) ? Reported(entry) : null;

    /// <summary>
    /// A run as the outside sees it: its own state plus how many of its processes are still alive.
    /// Asked at read time, because a task that exited leaving a server behind changes nothing about
    /// the run's state and everything about whether there is still something to stop.
    /// </summary>
    private static RunSummary Reported(Entry entry) =>
        entry.Summary with { Leftovers = entry.Group.LiveCount() };

    /// <summary>Checks out and plans, without running anything.</summary>
    public async Task<(RunSummary? Summary, string? Error)> PrepareAsync(RunArgs args)
    {
        var id = Guid.NewGuid().ToString("n")[..12];
        var entry = new Entry(id);
        _runs[id] = entry;

        return await PlanAsync(entry, args);
    }

    /// <summary>
    /// Supplies the values a config's inputs were missing and plans again.
    /// <para>
    /// The same run rather than a new one: the repository is already checked out, and a window that
    /// asked for a password should not leave a trail of abandoned runs behind. The plan is rebuilt
    /// with the values, so what the user approves is what those values produced.
    /// </para>
    /// </summary>
    public async Task<(RunSummary? Summary, string? Error)> SupplyInputsAsync(
        string id, IReadOnlyDictionary<string, string?> values)
    {
        if (!_runs.TryGetValue(id, out var entry)) return (null, "unknown run");
        if (entry.Args is not { } args) return (null, "this run cannot take inputs");
        if (entry.Summary.State is not (RunState.AwaitingInput or RunState.AwaitingConfirmation))
            return (entry.Summary, "this run has already started");

        // Later assignments win, so the supplied values override whatever was passed originally.
        var merged = args.Inputs
            .Concat(values.Select(v => $"{v.Key}={v.Value}"))
            .ToList();

        return await PlanAsync(entry, args with { Inputs = merged });
    }

    private async Task<(RunSummary? Summary, string? Error)> PlanAsync(Entry entry, RunArgs args)
    {
        entry.Remember(args);

        var git = new GitClient(
            new CredentialResolver(args.Token),
            onCheckoutProgress: (percent, detail) => entry.Publish(new RunEvent(
                RunEventKind.Progress, null, detail,
                new RunProgress(RunPhase.Checkout, ProgressModel.Total(RunPhase.Checkout, percent), detail))));

        var preparation = await Task.Run(() => RunPipeline.Prepare(args, store, git, (_, provided) => provided));

        if (preparation.ExitCode != 0 || preparation.Plan is null)
        {
            // A config whose inputs have no values is not a broken config: it is a form waiting to
            // be filled in, and the caller gets the fields rather than a dead end.
            if (preparation.Config is { Inputs.Count: > 0 } config)
            {
                entry.NeedsInput(config, preparation.Values, preparation.Error ?? "values are missing",
                    preparation.Origin.ToString().ToLowerInvariant());
                return (entry.Summary, preparation.Error);
            }

            entry.Fail(preparation.Error ?? "preparation failed");
            return (entry.Summary, preparation.Error);
        }

        entry.Prepared(preparation, Provisions(preparation.Config, Path.Combine(store.Root, "tools")));

        // Where the plan came from, before the first command scrolls past: a config derived from
        // another launcher's scripts, or from guessing, is not the same promise as a quickrun.yml.
        foreach (var note in preparation.Notes)
            entry.Publish(new RunEvent(RunEventKind.Info, null, note));

        return (entry.Summary, null);
    }

    /// <summary>
    /// What QuickRun would install before this run, in the words the confirmation window shows.
    /// Asked here rather than at execution time so the plan on screen is the whole plan.
    /// </summary>
    private static IReadOnlyList<string> Provisions(RunConfig? config, string toolRoot)
    {
        if (config is null || config.Requires.Count == 0) return Array.Empty<string>();

        return ToolChecker.CheckAll(config.Requires)
            .Select(check => Provisioner.PlanFor(check, toolRoot))
            .OfType<ProvisionPlan>()
            .Select(plan => $"{plan.Tool} {plan.Version} is missing - QuickRun will install it "
                            + $"into {plan.Directory} from {plan.Source}")
            .ToList();
    }

    /// <summary>Starts a prepared run. Returns false if the run is unknown or already started.</summary>
    public bool Confirm(string id)
    {
        if (!_runs.TryGetValue(id, out var entry)) return false;
        if (entry.Summary.State != RunState.AwaitingConfirmation) return false;

        entry.Begin();
        _ = Task.Run(() => ExecuteAsync(entry));
        return true;
    }

    public bool Stop(string id) => _runs.TryGetValue(id, out var entry) && entry.RequestStop();

    /// <summary>
    /// Takes a finished run off the list. Only a finished one: forgetting a run that is still going
    /// would leave its processes with nobody watching them and no way back to its log.
    /// </summary>
    public bool Forget(string id)
    {
        if (!_runs.TryGetValue(id, out var entry)) return false;
        if (entry.Summary.State is RunState.Running or RunState.Stopping
            or RunState.AwaitingConfirmation or RunState.AwaitingInput) return false;

        // A finished run whose processes are still alive keeps its place: taking it off the list
        // would drop the only handle left on them.
        if (entry.Group.LiveCount() > 0) return false;
        if (!_runs.TryRemove(id, out var removed)) return false;

        // Closed only here. While the run is on the list, what it started stays killable - which is
        // the whole reason the group is kept.
        removed.Group.Dispose();
        return true;
    }

    /// <summary>
    /// Throws away a prepared run nobody approved. Without this a declined plan would sit in the
    /// list as "awaiting confirmation" for ever, and the reader cannot tell that from a plan still
    /// waiting for them.
    /// </summary>
    public bool Cancel(string id) => _runs.TryGetValue(id, out var entry) && entry.CancelBeforeStart();

    public IAsyncEnumerable<RunEvent> Subscribe(string id, CancellationToken ct) =>
        _runs.TryGetValue(id, out var entry)
            ? entry.Subscribe(ct)
            : Empty();

    private static async IAsyncEnumerable<RunEvent> Empty()
    {
        await Task.CompletedTask;
        yield break;
    }

    private async Task ExecuteAsync(Entry entry)
    {
        var preparation = entry.Preparation!;
        var config = preparation.Config!;

        var secrets = Interpolator.Secrets(preparation.Values!, InputResolver.SecretIds(config.Inputs));
        var options = new RunOptions(
            preparation.Workspace!,
            new InterpolationContext(preparation.Values!, preparation.Workspace!,
                RunPipeline.RepoName(preparation.Plan!.Repo), preparation.Plan.Ref),
            InputResolver.ToEnv(config.Inputs, preparation.Values!),
            secrets,
            Readiness.DefaultTimeout,
            // A missing toolchain is installed here rather than reported: everything QuickRun puts
            // on a machine lives under its own root, and this is where that root is.
            ToolRoot: Path.Combine(store.Root, "tools"));

        var opened = new HashSet<string>(StringComparer.Ordinal);

        await using var runner = new Runner(e =>
        {
            entry.Publish(e);

            // The runner reports the address; opening it is a decision only the host makes, and
            // only once per address - a task that restarts must not open a second tab each time.
            if (openUrl is not null
                && e.Kind == RunEventKind.Info
                && e.Text.StartsWith("open ", StringComparison.Ordinal)
                && opened.Add(e.Text))
                openUrl(e.Text[5..].Trim());
        }, entry.Group);
        var outcome = await runner.ExecuteAsync(config, options, entry.StopToken);

        // The stop commands are the repository's code, and a run has to reach a final state even
        // when they hang - otherwise "stopping" is where it stays for ever.
        try
        {
            await runner.StopAsync().WaitAsync(StopTimeout);
        }
        catch (TimeoutException)
        {
            entry.Publish(new RunEvent(RunEventKind.Error, null,
                $"the stop commands did not finish within {StopTimeout.TotalSeconds:0}s - giving up on them"));
        }

        // The folder travels with the preparation, so recording the outcome does not turn a note
        // about somebody's working copy into a claim that QuickRun owns the directory.
        store.Touch(preparation.WorkspaceId!, preparation.Plan.Repo, preparation.Plan.Ref,
            preparation.Plan.Commit, outcome.Ok, preparation.LocalFolder);

        entry.Complete(outcome);
    }

    /// <summary>
    /// One run's state, its event history and its subscribers. History is replayed to a late
    /// subscriber, so an extension that connects after the run started still sees what happened.
    /// </summary>
    private sealed class Entry(string id)
    {
        private readonly CancellationTokenSource _stop = new();

        /// <summary>
        /// Every process this run started, for as long as the run is on the list. Not the runner's,
        /// because it has to outlive the runner: a task that launches a server and exits leaves that
        /// server running, and stopping afterwards has to be able to reach it.
        /// </summary>
        public ProcessGroup Group { get; } = ProcessGroup.Create();
        private int _live;
        private readonly List<RunEvent> _history = new();
        private readonly List<Channel<RunEvent>> _subscribers = new();
        private readonly object _gate = new();

        public RunSummary Summary { get; private set; } = new(
            id, "", "", null, id, RunState.AwaitingConfirmation,
            Array.Empty<PlannedCommand>(), "", null, null, null, null, 0);

        public RunPreparation? Preparation { get; private set; }

        /// <summary>What this run was asked for, so it can be planned again with more values.</summary>
        public RunArgs? Args { get; private set; }

        public void Remember(RunArgs args) => Args = args;

        /// <summary>
        /// The config declares inputs and the values are not there yet. Secrets are listed without
        /// their value: the form needs the field, and a password must not travel back out.
        /// </summary>
        public void NeedsInput(RunConfig config, IReadOnlyDictionary<string, string?>? values, string error,
            string origin)
        {
            var secrets = InputResolver.SecretIds(config.Inputs);
            var safe = (values ?? new Dictionary<string, string?>())
                .ToDictionary(v => v.Key, v => secrets.Contains(v.Key) ? null : v.Value, StringComparer.Ordinal);

            lock (_gate)
                Summary = Summary with
                {
                    State = RunState.AwaitingInput,
                    Error = error,
                    Description = config.Description,
                    Inputs = config.Inputs,
                    Values = safe,
                    Origin = origin,
                };
        }

        public CancellationToken StopToken => _stop.Token;

        public void Prepared(RunPreparation preparation, IReadOnlyList<string> provisions)
        {
            Preparation = preparation;
            var plan = preparation.Plan!;
            lock (_gate)
                Summary = Summary with
                {
                    Repo = plan.Repo,
                    Ref = plan.Ref,
                    Commit = plan.Commit,
                    DisplayName = plan.DisplayName,
                    Commands = plan.Commands,
                    Fingerprint = plan.Fingerprint,
                    State = RunState.AwaitingConfirmation,
                    Workspace = preparation.Workspace,
                    Description = preparation.Config?.Description,
                    Error = null,
                    Inputs = preparation.Config?.Inputs,
                    Values = Safe(preparation),
                    Origin = preparation.Origin.ToString().ToLowerInvariant(),
                    Provisions = provisions,
                    Tasks = preparation.Config?.Tasks
                        .Select(t => new RunTaskStatus(t.Name, "waiting", t.OpenUrl))
                        .ToList(),
                };
        }

        /// <summary>The values as they may be shown again, with secrets left out.</summary>
        private static IReadOnlyDictionary<string, string?>? Safe(RunPreparation preparation)
        {
            if (preparation.Values is not { } values || preparation.Config is not { } config) return null;

            var secrets = InputResolver.SecretIds(config.Inputs);
            return values.ToDictionary(v => v.Key, v => secrets.Contains(v.Key) ? null : v.Value,
                StringComparer.Ordinal);
        }

        public void Begin()
        {
            lock (_gate) Summary = Summary with { State = RunState.Running };
        }

        public void Fail(string error)
        {
            lock (_gate) Summary = Summary with { State = RunState.Failed, Error = error };
            Publish(new RunEvent(RunEventKind.Failed, null, error));
            CloseSubscribers();
        }

        public void Complete(RunOutcome outcome)
        {
            var cancelled = _stop.IsCancellationRequested;

            lock (_gate)
                Summary = Summary with
                {
                    State = cancelled ? RunState.Cancelled
                        : outcome.Ok ? RunState.Succeeded : RunState.Failed,
                    Error = outcome.Error,
                    LiveTasks = 0,
                };

            // The runner announces finishing and failing itself, but a run that was stopped returns
            // without a word - which left the log window saying "Running" for ever, with a Stop
            // button that had nothing left to stop.
            if (cancelled) Publish(new RunEvent(RunEventKind.Cancelled, null, "stopped on request"));

            CloseSubscribers();
        }

        public bool CancelBeforeStart()
        {
            lock (_gate)
            {
                if (Summary.State != RunState.AwaitingConfirmation) return false;
                Summary = Summary with { State = RunState.Cancelled };
            }

            CloseSubscribers();
            return true;
        }

        public bool RequestStop()
        {
            // Already asked once. The run is winding down, but a task may have left something behind
            // - and that is what a second Stop is for: killing what is still alive.
            if (_stop.IsCancellationRequested) return KillLeftovers();

            lock (_gate)
                if (Summary.State == RunState.Running)
                    Summary = Summary with { State = RunState.Stopping };

            Publish(new RunEvent(RunEventKind.Info, null, "stopping"));
            _stop.Cancel();

            // The commands take themselves down through their own cancellation. This also takes down
            // what they started and then stopped watching - a server launched in the background by a
            // task that has already exited used to survive a stop and keep answering.
            Group.Terminate();
            return true;
        }

        /// <summary>
        /// Kills what a finished run left running. A run whose tasks exited can still own processes,
        /// and until they are gone "stopped" is not true.
        /// </summary>
        private bool KillLeftovers()
        {
            var alive = Group.LiveCount();
            if (alive == 0) return false;

            Publish(new RunEvent(RunEventKind.Info, null,
                $"killing {alive} process(es) this run left running"));
            Group.Terminate();
            return true;
        }

        public void Publish(RunEvent e)
        {
            lock (_gate)
            {
                if (e.Progress is { } progress) Summary = Summary with { Progress = progress };

                // The runner announces the URL it would open as a log line. Lifting it into the
                // summary is what lets a window show "it is running here" instead of making the
                // reader find that line again in a few thousand lines of build output.
                if (e.Kind == RunEventKind.Info && e.Text.StartsWith("open ", StringComparison.Ordinal))
                    Summary = Summary with { Url = e.Text[5..].Trim() };
                else if (Summary.Url is null && LocalAddress.In(e.Text) is { } guessed)
                    Summary = Summary with { Url = guessed };

                // What "stop" would actually stop. A run whose tasks have all exited is still
                // Running while the runner winds down, and offering to stop nothing is a lie.
                if (e.Kind is RunEventKind.TaskStarted or RunEventKind.TaskExited)
                {
                    _live = Math.Max(0, _live + (e.Kind == RunEventKind.TaskStarted ? 1 : -1));
                    Summary = Summary with { LiveTasks = _live };
                }

                // Per task, because "running" for the whole run says nothing about which of five
                // services is up and where it is listening.
                if (e.Task is { } name)
                {
                    var state = e.Kind switch
                    {
                        RunEventKind.TaskStarted => "starting",
                        RunEventKind.TaskReady => "ready",
                        RunEventKind.TaskExited => "exited",
                        _ => null,
                    };

                    var address = e.Kind == RunEventKind.Info
                                  && e.Text.StartsWith("open ", StringComparison.Ordinal)
                        ? e.Text[5..].Trim()
                        : null;

                    var pid = e.Kind == RunEventKind.Info
                              && e.Text.StartsWith("pid ", StringComparison.Ordinal)
                              && int.TryParse(e.Text[4..].Trim(), out var parsed)
                        ? parsed
                        : (int?)null;

                    if (state is not null || address is not null || pid is not null)
                        Summary = Summary with { Tasks = Update(Summary.Tasks, name, state, address, pid) };
                }

                _history.Add(e);
                if (_history.Count > ReplayBufferSize) _history.RemoveAt(0);

                foreach (var subscriber in _subscribers) subscriber.Writer.TryWrite(e);
            }
        }

        /// <summary>
        /// One task's row, replaced rather than mutated - the summary is a record everyone else
        /// reads without locking.
        /// </summary>
        private static IReadOnlyList<RunTaskStatus> Update(
            IReadOnlyList<RunTaskStatus>? tasks, string name, string? state, string? url, int? pid)
        {
            var rows = tasks?.ToList() ?? new List<RunTaskStatus>();
            var at = rows.FindIndex(t => t.Name == name);

            if (at < 0)
            {
                rows.Add(new RunTaskStatus(name, state ?? "starting", url, pid));
                return rows;
            }

            // A process that is gone stays gone. Readiness can still fire afterwards - a task that
            // only launches something, `docker compose up -d`, exits long before its port opens -
            // and that is worth the address it brings, but not a state that says it is still up.
            var next = rows[at].State == "exited" && state == "ready" ? "exited" : state ?? rows[at].State;

            rows[at] = rows[at] with
            {
                State = next,
                Url = url ?? rows[at].Url,
                // A restarted task runs as a new process, so a new pid replaces the old one.
                Pid = pid ?? rows[at].Pid,
            };

            return rows;
        }

        public async IAsyncEnumerable<RunEvent> Subscribe(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            var channel = Channel.CreateUnbounded<RunEvent>();
            List<RunEvent> replay;

            lock (_gate)
            {
                replay = _history.ToList();

                // A run that is already over gets its history and then the end of the stream.
                // Handing a late reader an open channel that nothing will ever write to again left
                // it waiting for ever - a log window opened on a finished run never said finished,
                // and the connection behind it stayed up until something else closed it.
                if (Summary.State is RunState.Succeeded or RunState.Failed or RunState.Cancelled)
                    channel.Writer.TryComplete();

                _subscribers.Add(channel);
            }

            try
            {
                foreach (var e in replay) yield return e;

                await foreach (var e in channel.Reader.ReadAllAsync(ct)) yield return e;
            }
            finally
            {
                lock (_gate) _subscribers.Remove(channel);
            }
        }

        private void CloseSubscribers()
        {
            lock (_gate)
                foreach (var subscriber in _subscribers) subscriber.Writer.TryComplete();
        }
    }
}
