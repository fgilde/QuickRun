using QuickRun.Core.Config;
using QuickRun.Core.Process;
using QuickRun.Core.Requires;

namespace QuickRun.Core.Tests;

public class ToolCheckerTests
{
    private static ToolRequirement Req(string tool, string? version = null, bool optional = false)
        => new(tool, version, null, optional);

    private static Func<string, string[], CommandResult> Fake(string output, int exit = 0)
        => (_, _) => new CommandResult(exit, output, false);

    [Theory]
    [InlineData("node", "-v")]
    [InlineData("npm", "-v")]
    [InlineData("dotnet", "--version")]
    [InlineData("java", "-version")]
    [InlineData("go", "version")]
    [InlineData("some-random-tool", "--version")]
    public void ProbeArgs_knows_the_common_tools(string tool, string expected)
        => Assert.Equal(expected, ToolChecker.ProbeArgs(tool).Single());

    [Fact]
    public void A_satisfied_requirement_reports_the_found_version()
    {
        var r = ToolChecker.Check(Req("dotnet", ">=9.0"), Fake("10.0.300"));
        Assert.True(r.Found);
        Assert.Equal("10.0.300", r.FoundVersion);
        Assert.True(r.Satisfied);
        Assert.False(r.Blocks);
    }

    [Fact]
    public void A_version_below_the_range_is_not_satisfied()
    {
        var r = ToolChecker.Check(Req("dotnet", ">=9.0"), Fake("8.0.404"));
        Assert.True(r.Found);
        Assert.False(r.Satisfied);
        Assert.True(r.Blocks);
    }

    [Fact]
    public void A_missing_tool_is_not_found()
    {
        var r = ToolChecker.Check(Req("nope"), Fake("command not found", exit: 127));
        Assert.False(r.Found);
        Assert.Null(r.FoundVersion);
        Assert.True(r.Blocks);
    }

    [Fact]
    public void A_missing_optional_tool_does_not_block()
        => Assert.False(ToolChecker.Check(Req("nope", optional: true), Fake("", exit: 127)).Blocks);

    [Fact]
    public void A_tool_present_without_a_version_requirement_is_satisfied()
    {
        var r = ToolChecker.Check(Req("docker"), Fake("Docker version 27.3.1, build ce12230"));
        Assert.True(r.Satisfied);
        Assert.Equal("27.3.1", r.FoundVersion);
    }

    [Fact]
    public void A_tool_that_exits_zero_but_prints_no_version_is_still_found()
    {
        var r = ToolChecker.Check(Req("weird"), Fake("hello"));
        Assert.True(r.Found);
        Assert.Null(r.FoundVersion);
        Assert.True(r.Satisfied);
    }

    [Fact]
    public void A_tool_that_exits_zero_without_a_version_fails_a_version_requirement()
        => Assert.False(ToolChecker.Check(Req("weird", ">=1.0"), Fake("hello")).Satisfied);

    [Fact]
    public void Describe_mentions_the_tool_and_the_outcome()
    {
        Assert.Contains("not found on PATH", ToolChecker.Check(Req("nope"), Fake("", exit: 127)).Describe());
        Assert.Contains("10.0.300", ToolChecker.Check(Req("dotnet", ">=9.0"), Fake("10.0.300")).Describe());
    }

    [Fact]
    public void CheckAll_returns_one_result_per_requirement()
        => Assert.Equal(2, ToolChecker.CheckAll(new[] { Req("git"), Req("dotnet") }).Count);
}
