using QuickRun.App.Ui;

namespace QuickRun.App.Tests;

/// <summary>
/// Which thread the interface loop runs on.
/// <para>
/// The failure this exists for: on macOS the loop ran on a thread-pool thread, AppKit refused it
/// with "IDispatcherImpl belongs to a different thread", and typing <c>quickrun</c> in a terminal
/// produced that error and no window. AppKit may only be driven from the thread the process started
/// on, so that thread has to be the one the loop gets.
/// </para>
/// </summary>
public class UiHostTests
{
    [Fact]
    public void The_loop_runs_on_the_thread_that_owned_the_process()
    {
        // Windows is the exception on purpose: there the loop needs a single-threaded-apartment
        // thread of its own, which the next test checks.
        if (OperatingSystem.IsWindows()) return;

        var owner = Environment.CurrentManagedThreadId;
        var ranOn = 0;

        var exit = UiHost.Own(() =>
        {
            // As in the real command: this part is on a worker, and the loop must not be.
            Assert.NotEqual(owner, Environment.CurrentManagedThreadId);

            UiHost.RunLoop(() => ranOn = Environment.CurrentManagedThreadId);
            return 7;
        });

        Assert.Equal(7, exit);
        Assert.Equal(owner, ranOn);
    }

    [Fact]
    public void On_windows_the_loop_gets_an_apartment_thread_of_its_own()
    {
        if (!OperatingSystem.IsWindows()) return;

        var caller = Environment.CurrentManagedThreadId;
        var ranOn = 0;
        var apartment = ApartmentState.Unknown;

        UiHost.RunLoop(() =>
        {
            ranOn = Environment.CurrentManagedThreadId;
            apartment = Thread.CurrentThread.GetApartmentState();
        });

        Assert.NotEqual(caller, ranOn);
        Assert.Equal(ApartmentState.STA, apartment);
    }

    [Fact]
    public void A_command_that_never_asks_for_a_loop_still_returns_its_exit_code()
    {
        // Every other command - run, validate, detect - goes through Own without ever handing back
        // a loop, and must not leave the process waiting for one.
        Assert.Equal(3, UiHost.Own(() => 3));
    }

    [Fact]
    public void The_first_thread_is_required_everywhere_except_windows()
        => Assert.Equal(!OperatingSystem.IsWindows(), UiHost.WantsFirstThread);
}
