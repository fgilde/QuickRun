using QuickRun.Core.Run;

namespace QuickRun.App.Commands;

/// <summary>
/// What <c>quickrun run --smoke</c> asks of a run: every task ready, nothing failed. It watches the
/// event stream and says when the run can end, which is either the moment the last task is ready or
/// the first sign that it never will be.
/// </summary>
internal sealed class SmokeCheck(int tasks)
{
    private readonly HashSet<string> _ready = new(StringComparer.Ordinal);

    public string? Problem { get; private set; }

    public bool AllReady => _ready.Count >= tasks;

    /// <summary>Takes one event; true when the run has said all it is going to.</summary>
    public bool See(RunEvent e)
    {
        switch (e.Kind)
        {
            case RunEventKind.TaskReady when e.Task is not null:
                _ready.Add(e.Task);
                break;
            case RunEventKind.Failed:
                Problem ??= Prefixed(e);
                break;
            // A readiness check that gave up leaves the task running, which a person can still use
            // and a check cannot call a pass.
            case RunEventKind.Error when e.Text.StartsWith("gave up waiting", StringComparison.Ordinal):
                Problem ??= Prefixed(e);
                break;
        }

        return Problem is not null || AllReady;
    }

    /// <summary>Null when the config passed, otherwise what went wrong.</summary>
    public string? Verdict(RunOutcome outcome, int timeoutMinutes)
    {
        if (Problem is not null) return Problem;
        if (AllReady) return null;

        // Every task exited on its own and cleanly: a config of one-shot commands, which passed.
        if (outcome.Ok) return null;

        return outcome.Error == "run cancelled"
            ? $"not every task was ready after {timeoutMinutes} minute(s)"
            : outcome.Error ?? "the run failed";
    }

    private static string Prefixed(RunEvent e) => e.Task is null ? e.Text : $"{e.Task}: {e.Text}";
}
