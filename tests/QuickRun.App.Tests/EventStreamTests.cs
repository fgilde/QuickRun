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
/// </summary>
public class EventStreamTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(50);

    /// <summary>An event source that can be made to wait, so silence is something a test can cause.</summary>
    private sealed class Source : IAsyncEnumerator<RunEvent>
    {
        private readonly Queue<RunEvent> _events;
        private readonly TimeSpan _silence;
        private bool _first = true;

        public Source(TimeSpan silence, params RunEvent[] events)
        {
            _silence = silence;
            _events = new Queue<RunEvent>(events);
        }

        public RunEvent Current { get; private set; } = null!;

        public async ValueTask<bool> MoveNextAsync()
        {
            // The silence comes before the first event, which is the case that broke: a build that
            // says nothing for a long time and then starts talking.
            if (_first)
            {
                _first = false;
                await Task.Delay(_silence);
            }

            if (_events.Count == 0) return false;

            Current = _events.Dequeue();
            return true;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task A_quiet_run_still_produces_traffic()
    {
        var frames = new List<string>();
        var source = new Source(Tick * 6, new RunEvent(RunEventKind.Output, "build", "done at last"));

        await DaemonHost.PumpAsync(source, frame => { frames.Add(frame); return Task.CompletedTask; },
            Tick, CancellationToken.None);

        Assert.Contains(": keepalive\n\n", frames);

        // Several of them, because one would only prove the first ten seconds of a long build.
        Assert.True(frames.Count(f => f.StartsWith(':')) >= 2,
            $"expected repeated keepalives, got {frames.Count(f => f.StartsWith(':'))}");

        // And the event itself still arrives, after the keepalives rather than instead of them.
        Assert.Equal("data: ", frames[^1][..6]);
        Assert.Contains("done at last", frames[^1]);
    }

    /// <summary>
    /// A restore prints thousands of lines a second. Every one of those used to leave a ten-second
    /// timer behind in the first version of this, which is its own kind of leak.
    /// </summary>
    [Fact]
    public async Task A_stream_with_something_to_say_says_only_that()
    {
        var frames = new List<string>();

        var chatty = new Source(TimeSpan.Zero,
            Enumerable.Range(0, 200)
                .Select(i => new RunEvent(RunEventKind.Output, "restore", $"line {i}"))
                .ToArray());

        await DaemonHost.PumpAsync(chatty, frame => { frames.Add(frame); return Task.CompletedTask; },
            Tick, CancellationToken.None);

        Assert.Equal(200, frames.Count);
        Assert.DoesNotContain(frames, f => f.StartsWith(':'));
    }

    [Fact]
    public async Task A_stream_that_ends_ends()
    {
        var frames = new List<string>();

        await DaemonHost.PumpAsync(new Source(TimeSpan.Zero), frame => { frames.Add(frame); return Task.CompletedTask; },
            Tick, CancellationToken.None);

        Assert.Empty(frames);
    }
}
