using System.Collections.Concurrent;
using QuickRun.App.Daemon;
using QuickRun.Core.Run;

namespace QuickRun.App.Tests;

/// <summary>
/// The event stream, and the reason it has to say something while a run is quiet.
/// <para>
/// A run of a large repository prints nothing for minutes while it builds. The stream used to send
/// no bytes at all for those minutes; a browser extension's service worker is shut down after thirty
/// seconds without traffic, so the worker reading the stream was killed and the log window kept the
/// last percentage it had heard - which looked exactly like a run frozen at 85% while the build went
/// on underneath.
/// </para>
/// <para>
/// Nothing here measures elapsed time. Silence is held open until the test has seen what it came
/// for, and released deliberately: a test that asserts "at least two keepalives in 300ms" is a test
/// that fails on a loaded build agent for no reason.
/// </para>
/// </summary>
public class EventStreamTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(20);

    /// <summary>
    /// An event source that says nothing until it is let go, so a test can cause silence rather
    /// than wait for it.
    /// </summary>
    private sealed class Held(Task release, params RunEvent[] events) : IAsyncEnumerator<RunEvent>
    {
        private readonly Queue<RunEvent> _events = new(events);
        private bool _held;

        public RunEvent Current { get; private set; } = null!;

        public async ValueTask<bool> MoveNextAsync()
        {
            // Once, before the first event: a build that says nothing for a long time and then
            // starts talking.
            if (!_held)
            {
                _held = true;
                await release;
            }

            if (_events.Count == 0) return false;

            Current = _events.Dequeue();
            return true;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>An event source with everything ready at once, and no awaiting anywhere.</summary>
    private sealed class Ready(params RunEvent[] events) : IAsyncEnumerator<RunEvent>
    {
        private readonly Queue<RunEvent> _events = new(events);

        public RunEvent Current { get; private set; } = null!;

        public ValueTask<bool> MoveNextAsync()
        {
            if (_events.Count == 0) return ValueTask.FromResult(false);

            Current = _events.Dequeue();
            return ValueTask.FromResult(true);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task A_quiet_run_still_produces_traffic()
    {
        var frames = new ConcurrentQueue<string>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var source = new Held(release.Task, new RunEvent(RunEventKind.Output, "build", "done at last"));

        var pump = DaemonHost.PumpAsync(source, frame =>
        {
            frames.Enqueue(frame);
            return Task.CompletedTask;
        }, Tick, CancellationToken.None);

        // Two of them, because one would only prove the first interval of a long silence. Waited
        // for rather than timed: if they never come, this is what fails.
        using var giveUp = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        while (frames.Count(f => f.StartsWith(':')) < 2)
            await Task.Delay(10, giveUp.Token);

        release.SetResult();
        await pump;

        Assert.Contains(": keepalive\n\n", frames);

        // And the event itself still arrives, after the keepalives rather than instead of them.
        var last = frames.Last();
        Assert.StartsWith("data: ", last);
        Assert.Contains("done at last", last);
    }

    /// <summary>
    /// A restore prints thousands of lines a second. Every one of those used to leave a timer behind
    /// in the first version of this, which is its own kind of leak.
    /// </summary>
    [Fact]
    public async Task A_stream_with_something_to_say_says_only_that()
    {
        var frames = new List<string>();

        var chatty = new Ready(Enumerable.Range(0, 200)
            .Select(i => new RunEvent(RunEventKind.Output, "restore", $"line {i}"))
            .ToArray());

        await DaemonHost.PumpAsync(chatty, frame =>
        {
            frames.Add(frame);
            return Task.CompletedTask;
        }, Tick, CancellationToken.None);

        Assert.Equal(200, frames.Count);
        Assert.DoesNotContain(frames, f => f.StartsWith(':'));
    }

    [Fact]
    public async Task A_stream_that_ends_ends()
    {
        var frames = new List<string>();

        await DaemonHost.PumpAsync(new Ready(), frame =>
        {
            frames.Add(frame);
            return Task.CompletedTask;
        }, Tick, CancellationToken.None);

        Assert.Empty(frames);
    }
}
