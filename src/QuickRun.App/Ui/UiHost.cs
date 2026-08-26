using System.Collections.Concurrent;

namespace QuickRun.App.Ui;

/// <summary>
/// Decides which thread the user interface loop runs on, because the two desktop platforms disagree
/// about it in incompatible ways.
/// <para>
/// macOS is the strict one: AppKit may only be driven from the process's first thread. Running the
/// loop anywhere else fails with "IDispatcherImpl belongs to a different thread" and nothing appears
/// at all - which is exactly what <c>quickrun</c> did in a terminal on a Mac.
/// </para>
/// <para>
/// Windows is the opposite: the loop may live on any thread, but the system WebView the window hosts
/// refuses a thread in the multi-threaded apartment, and by the time an async command runs, the
/// thread it is on is a thread-pool thread. So there the loop gets a single-threaded-apartment
/// thread of its own.
/// </para>
/// <para>
/// Hence this: where the first thread is required, the command runs on a worker and the first thread
/// waits here to be handed the loop; where it is not, the loop gets its own thread.
/// </para>
/// </summary>
public static class UiHost
{
    /// <summary>Whether the platform insists the loop runs on the thread the process started on.</summary>
    public static bool WantsFirstThread => !OperatingSystem.IsWindows();

    private static readonly BlockingCollection<Action> Handovers = new();
    private static volatile bool _waiting;

    /// <summary>
    /// Runs <paramref name="command"/> - the whole command line - and keeps the calling thread free
    /// for the loop. Called from <c>Main</c>, so the calling thread is the process's first.
    /// </summary>
    public static int Own(Func<int> command)
    {
        var exit = 2;
        var finished = new ManualResetEventSlim(false);

        var worker = new Thread(() =>
        {
            try
            {
                exit = command();
            }
            finally
            {
                // Whatever happened, stop waiting for a loop that is never coming.
                finished.Set();
                Handovers.CompleteAdding();
            }
        }, 16 * 1024 * 1024)
        {
            Name = "QuickRun command",
            IsBackground = false,
        };

        _waiting = true;
        worker.Start();

        // Usually exactly one handover - the tray's loop - and it runs until the user quits.
        foreach (var loop in Handovers.GetConsumingEnumerable()) loop();

        _waiting = false;
        finished.Wait();
        return exit;
    }

    /// <summary>
    /// Runs the loop where this platform demands, and returns when it has finished.
    /// </summary>
    public static void RunLoop(Action loop)
    {
        if (_waiting)
        {
            var finished = new ManualResetEventSlim(false);

            Handovers.Add(() =>
            {
                try { loop(); }
                finally { finished.Set(); }
            });

            finished.Wait();
            return;
        }

        // Windows, or a caller that already owns a suitable thread.
        var ui = new Thread(() => loop(), 16 * 1024 * 1024)
        {
            Name = "QuickRun UI",
            IsBackground = false,
        };

        if (OperatingSystem.IsWindows()) ui.SetApartmentState(ApartmentState.STA);

        ui.Start();
        ui.Join();
    }
}
