using QuickRun.Core.Process;

namespace QuickRun.Core.Tests;

public class ForegroundTests
{
    private static Foreground.WindowOwner P(int pid, int parent, nint window = 0) => new(pid, parent, window);

    [Fact]
    public void The_window_of_a_grandchild_is_found()
    {
        // What a task actually looks like: shell -> dotnet run -> the application.
        var processes = new[]
        {
            P(100, 1),
            P(200, 100),
            P(300, 200, window: 42),
            P(400, 1, window: 99), // an unrelated application the user had open
        };

        Assert.Equal(42, Foreground.PickWindow(processes, rootPid: 100));
    }

    [Fact]
    public void Nothing_outside_the_tree_is_ever_raised()
    {
        var processes = new[] { P(100, 1), P(400, 1, window: 99) };

        Assert.Equal(0, Foreground.PickWindow(processes, rootPid: 100));
    }

    [Fact]
    public void The_outermost_window_wins()
    {
        // A splash screen started by the app must not beat the app's own window.
        var processes = new[] { P(100, 1, window: 7), P(200, 100, window: 8) };

        Assert.Equal(7, Foreground.PickWindow(processes, rootPid: 100));
    }

    [Fact]
    public void A_cycle_in_the_process_table_does_not_hang()
    {
        // Process ids are reused, so a snapshot can claim a process is its own ancestor.
        var processes = new[] { P(100, 200), P(200, 100) };

        Assert.Equal(0, Foreground.PickWindow(processes, rootPid: 100));
    }
}
