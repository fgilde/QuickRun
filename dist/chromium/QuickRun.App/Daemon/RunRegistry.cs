using System.Collections.Concurrent;
using System.Threading.Channels;
using QuickRun.App.Commands;
using QuickRun.Core.Config;
using QuickRun.Core.Git;
using QuickRun.Core.Inputs;
using QuickRun.Core.Run;
using QuickRun.Core.Workspace;

namespace QuickRun.App.Daemon;

public enum RunState
{
    /// <summary>Prepared and waiting for someone to approve the command list.</summary>
    AwaitingConfirmation,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

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
    string? Error);

/// <summary>
/// Tracks the runs the listener has been asked for.
/// <para>
/// Preparation and execution are deliberately separate: <see cref="PrepareAsync"/> checks the
/// repository out and builds the plan but executes nothing, and <see cref="Confirm"/> is what
/// starts it. That keeps the confirmation gate in place even though the browser, not a desktop
/// dialog, is what shows the commands.
/// </para>
/// </summary>
public sealed class RunRegistry(WorkspaceStore store)
{
    private const int ReplayBufferSize = 500;

    private readonly ConcurrentDictionary<string, Entry> _runs = new(StringComparer.Ordinal);

    public bool AnyActive => _runs.Values.Any(e => e.Summary.State is RunState.Running or RunState.AwaitingConfirmation);

    public IReadOnlyList<RunSummary> All() => _runs.Values.Select(e => e.Summary).ToList();

    public RunSummary? Get(string id) => _runs.TryGetValue(id, out var entry) ? entry.Summary : null;

    /// <summary>Checks out and plans, without running anything.</summary>
    public async Task<(RunSummary? Summary, string? Error)> PrepareAsync(RunArgs args)
    {
        var id = Guid.NewGuid().ToString("n")[..12];
        var entry = new Entry(id);
        _runs[id] = entry;

        var git = new GitClient(
            new CredentialResolver(args.Token),
            onCheckoutProgress: (percent, detail) => entry.Publish(new RunEvent(
                RunEventKind.Progress, null, detail,
                new RunProgress(RunPhase.Checkout, ProgressModel.Total(RunPhase.Checkout, percent), detail))));

        var preparation = await Task.Run(() => RunPipeline.Prepare(args, store, git, (_, provided) => provided));

        if (preparation.ExitCode != 0 || preparation.Plan is null)
        {
            entry.Fail(preparation.Error ?? "preparation failed");
            return (entry.Summary, preparation.Error);
        }

        entry.Prepared(preparation);
        return (entry.Summary, null);
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
            Readiness.DefaultTimeout);

        await using var runner = new Runner(entry.Publish);
        var outcome = await runner.ExecuteAsync(config, options, entry.StopToken);
        await runner.StopAsync();

        store.Touch(WorkspaceStore.IdFor(preparation.Plan.Repo, preparation.Plan.Ref),
            preparation.Plan.Repo, preparation.Plan.Ref, preparation.Plan.Commit, outcome.Ok);

        entry.Complete(outcome);
    }

    /// <summary>
    /// One run's state, its event history and its subscribers. History is replayed to a late
    /// subscriber, so an extension that connects after the run started still sees what happened.
    /// </summary>
    private sealed class Entry(string id)
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly List<RunEvent> _history = new();
        private readonly List<Channel<RunEvent>> _subscribers = new();
        private readonly object _gate = new();

        public RunSummary Summary { get; private set; } = new(
            id, "", "", null, id, RunState.AwaitingConfirmation,
            Array.Empty<PlannedCommand>(), "", null, null);

        public RunPreparation? Preparation { get; private set; }

        public CancellationToken StopToken => _stop.Token;

        public void Prepared(RunPreparation preparation)
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
                };
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
            lock (_gate)
                Summary = Summary with
                {
                    State = _stop.IsCancellationRequested ? RunState.Cancelled
                        : outcome.Ok ? RunState.Succeeded : RunState.Failed,
                    Error = outcome.Error,
                };
            CloseSubscribers();
        }

        public bool RequestStop()
        {
            if (_stop.IsCancellationRequested) return false;
            _stop.Cancel();
            return true;
        }

        public void Publish(RunEvent e)
        {
            lock (_gate)
            {
                if (e.Progress is { } progress) Summary = Summary with { Progress = progress };

                _history.Add(e);
                if (_history.Count > ReplayBufferSize) _history.RemoveAt(0);

                foreach (var subscriber in _subscribers) subscriber.Writer.TryWrite(e);
            }
        }

        public async IAsyncEnumerable<RunEvent> Subscribe(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            var channel = Channel.CreateUnbounded<RunEvent>();
            List<RunEvent> replay;

            lock (_gate)
            {
                replay = _history.ToList();
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
