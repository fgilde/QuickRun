using QuickRun.App.Commands;

namespace QuickRun.App.Tests;

/// <summary>
/// What a <c>quickrun://</c> link is allowed to carry.
/// <para>
/// A badge in a README is written by whoever owns that repository, and the link a browser hands over
/// is a string from a page. So this is a trust boundary: the link may say which repository to look
/// at, and nothing else. It never starts anything either - what comes out of here is only a target
/// for the local window, which still asks before running a command.
/// </para>
/// </summary>
public class RunTargetTests
{
    private static string? From(string url) => RunTarget.From(new Uri(url));

    [Fact]
    public void A_repository_shorthand_is_carried_through()
    {
        Assert.Equal("repo=fgilde%2FQuickRun", From("quickrun://run?repo=fgilde/QuickRun"));
    }

    [Fact]
    public void A_ref_and_a_pull_request_come_along()
    {
        Assert.Equal("repo=fgilde%2FQuickRun&ref=main&pr=12",
            From("quickrun://run?repo=fgilde/QuickRun&ref=main&pr=12"));
    }

    /// <summary>Everything but repo, ref and pr is dropped - silently, because it was never ours.</summary>
    [Theory]
    [InlineData("quickrun://run?repo=fgilde/QuickRun&token=hunter2")]
    [InlineData("quickrun://run?repo=fgilde/QuickRun&command=rm+-rf+%2F")]
    [InlineData("quickrun://run?repo=fgilde/QuickRun&config=run%3A+evil")]
    [InlineData("quickrun://run?repo=fgilde/QuickRun&path=C%3A%5CWindows")]
    public void Nothing_else_a_link_carries_survives(string url)
    {
        Assert.Equal("repo=fgilde%2FQuickRun", From(url));
    }

    [Theory]
    [InlineData("quickrun://run?repo=file%3A%2F%2F%2FC%3A%2FWindows")]
    [InlineData("quickrun://run?repo=http%3A%2F%2Fexample.com%2Fx.git")]
    [InlineData("quickrun://run?repo=")]
    [InlineData("quickrun://run")]
    public void A_repository_that_is_not_a_repository_is_no_target(string url)
    {
        Assert.Null(From(url));
    }

    /// <summary>
    /// The scheme has one other job - starting the daemon - and those URLs name no repository.
    /// </summary>
    [Fact]
    public void A_link_that_is_not_a_run_is_no_target()
    {
        Assert.Null(From("quickrun://start"));
        Assert.Null(From("quickrun://open?repo=fgilde/QuickRun"));
    }

    /// <summary>A ref is a branch name, not a place to hide a kilobyte of anything.</summary>
    [Fact]
    public void An_absurdly_long_ref_is_left_behind()
    {
        Assert.Equal("repo=fgilde%2FQuickRun",
            From($"quickrun://run?repo=fgilde/QuickRun&ref={new string('x', 400)}"));
    }
}
