using QuickRun.Core.Run;

namespace QuickRun.Core.Tests;

public class ProgressModelTests
{
    [Theory]
    [InlineData(RunPhase.Checkout, 0, 0)]
    [InlineData(RunPhase.Checkout, 50, 15)]
    [InlineData(RunPhase.Checkout, 100, 30)]
    [InlineData(RunPhase.Setup, 0, 30)]
    [InlineData(RunPhase.Setup, 50, 50)]
    [InlineData(RunPhase.Setup, 100, 70)]
    [InlineData(RunPhase.Tasks, 0, 70)]
    [InlineData(RunPhase.Tasks, 100, 100)]
    public void Total_weights_the_phases(RunPhase phase, int phasePercent, int expected)
        => Assert.Equal(expected, ProgressModel.Total(phase, phasePercent));

    [Theory]
    [InlineData(-20)]
    [InlineData(500)]
    public void Total_clamps_nonsense_input(int phasePercent)
    {
        var total = ProgressModel.Total(RunPhase.Setup, phasePercent);
        Assert.InRange(total, 30, 70);
    }

    [Fact]
    public void Total_never_goes_backwards_across_phases()
    {
        var values = new[]
        {
            ProgressModel.Total(RunPhase.Checkout, 100),
            ProgressModel.Total(RunPhase.Setup, 0),
            ProgressModel.Total(RunPhase.Setup, 100),
            ProgressModel.Total(RunPhase.Tasks, 0),
        };
        Assert.Equal(values.OrderBy(v => v), values);
    }

    [Theory]
    [InlineData(0, 5, 0)]
    [InlineData(2, 5, 40)]
    [InlineData(5, 5, 100)]
    [InlineData(1, 0, 100)]
    public void StepPercent_maps_step_counts(int done, int total, int expected)
        => Assert.Equal(expected, ProgressModel.StepPercent(done, total));

    [Fact]
    public void The_weights_add_up_to_a_hundred()
        => Assert.Equal(100, ProgressModel.CheckoutWeight + ProgressModel.SetupWeight + ProgressModel.TasksWeight);
}

public class GitProgressTests
{
    [Fact]
    public void Receiving_objects_maps_into_its_band()
    {
        var parsed = GitProgress.Parse("Receiving objects:  50% (500/1000), 1.20 MiB | 2.00 MiB/s");
        Assert.NotNull(parsed);
        Assert.Equal(55, parsed!.Value.Percent);
        Assert.Contains("50%", parsed.Value.Detail);
    }

    [Fact]
    public void A_remote_prefixed_line_is_still_recognised()
    {
        var parsed = GitProgress.Parse("remote: Compressing objects:  50% (5/10)");
        Assert.NotNull(parsed);
        Assert.Equal(15, parsed!.Value.Percent);
    }

    [Theory]
    [InlineData("Counting objects: 100%, done.", 10)]
    [InlineData("Receiving objects: 100% (1000/1000), done.", 90)]
    [InlineData("Resolving deltas: 100% (120/120), done.", 100)]
    [InlineData("Updating files: 100% (42/42), done.", 100)]
    public void Each_stage_ends_at_its_upper_bound(string line, int expected)
        => Assert.Equal(expected, GitProgress.Parse(line)!.Value.Percent);

    [Fact]
    public void Stages_are_monotonic_in_the_order_git_reports_them()
    {
        var lines = new[]
        {
            "Counting objects:  50%",
            "Counting objects: 100%",
            "remote: Compressing objects:  40%",
            "Receiving objects:   5%",
            "Receiving objects:  99%",
            "Resolving deltas:  50%",
            "Resolving deltas: 100%",
        };
        var percents = lines.Select(l => GitProgress.Parse(l)!.Value.Percent).ToArray();
        Assert.Equal(percents.OrderBy(p => p), percents);
    }

    [Theory]
    [InlineData("Cloning into 'app'...")]
    [InlineData("fatal: repository not found")]
    [InlineData("")]
    [InlineData("Submodule path 'x': checked out 'abc'")]
    public void Lines_without_a_counter_yield_nothing(string line)
        => Assert.Null(GitProgress.Parse(line));

    [Fact]
    public void An_unknown_stage_with_a_percentage_is_ignored()
        => Assert.Null(GitProgress.Parse("Doing something odd:  50%"));
}
