using QuickRun.App.Commands;
using QuickRun.Core.Run;

namespace QuickRun.App.Tests;

public class SmokeCheckTests
{
    private static RunEvent Ready(string task) => new(RunEventKind.TaskReady, task, "ready");

    [Fact]
    public void It_passes_once_every_task_is_ready_and_not_before()
    {
        var check = new SmokeCheck(2);

        Assert.False(check.See(Ready("web")));
        Assert.True(check.See(Ready("worker")));
        Assert.Null(check.Verdict(new RunOutcome(false, "run cancelled"), 20));
    }

    [Fact]
    public void A_task_that_failed_fails_it_at_once()
    {
        var check = new SmokeCheck(2);

        Assert.True(check.See(new RunEvent(RunEventKind.Failed, "web", "exited with code 1")));
        Assert.Equal("web: exited with code 1", check.Verdict(new RunOutcome(false, "run cancelled"), 20));
    }

    [Fact]
    public void A_readiness_check_that_gave_up_is_not_a_pass()
    {
        var check = new SmokeCheck(1);

        Assert.True(check.See(new RunEvent(RunEventKind.Error, "web", "gave up waiting for http://localhost:8000 after 300s")));
        Assert.NotNull(check.Verdict(new RunOutcome(false, "run cancelled"), 20));
    }

    [Fact]
    public void Running_out_of_time_says_so()
        => Assert.Equal("not every task was ready after 20 minute(s)",
            new SmokeCheck(1).Verdict(new RunOutcome(false, "run cancelled"), 20));

    [Fact]
    public void One_shot_tasks_that_all_exit_cleanly_pass()
        => Assert.Null(new SmokeCheck(1).Verdict(new RunOutcome(true, null), 20));
}
