using QuickRun.Core.Config;
using QuickRun.Core.Requires;

namespace QuickRun.Core.Tests;

/// <summary>
/// Installing what a repository needs, rather than telling someone to go and install it.
/// <para>
/// Nothing here downloads anything: what is worth pinning down is which requirement turns into
/// which install, and - more importantly - which ones must not turn into one at all.
/// </para>
/// </summary>
public class ProvisionerTests
{
    private static ToolCheckResult Missing(string tool, string? version, bool optional = false) =>
        new(new ToolRequirement(tool, version, null, optional), Found: false, null, Satisfied: false);

    [Theory]
    [InlineData("dotnet", true)]
    [InlineData("DotNet", true)]
    [InlineData("node", true)]
    [InlineData("python", false)]
    [InlineData("docker", false)]
    public void It_only_claims_the_tools_it_can_actually_install(string tool, bool handled)
    {
        Assert.Equal(handled, Provisioner.Handles(tool));
    }

    /// <summary>
    /// The installer takes a channel, a config states a range. ">=10" and "10.0.100" both mean the
    /// 10.0 channel; a version nobody stated means the long-term one.
    /// </summary>
    [Theory]
    [InlineData(">=10", "10.0")]
    [InlineData("10", "10.0")]
    [InlineData("10.0", "10.0")]
    [InlineData("8.0.404", "8.0")]
    [InlineData(null, "LTS")]
    [InlineData("", "LTS")]
    [InlineData("whatever", "LTS")]
    public void A_version_range_becomes_the_channel_it_means(string? version, string channel)
    {
        Assert.Equal(channel, Provisioner.Channel(version));
    }

    [Fact]
    public void A_missing_tool_becomes_a_plan_that_says_where_it_would_go()
    {
        var plan = Provisioner.PlanFor(Missing("dotnet", ">=10"), Path.Combine("C:", "quickrun", "tools"));

        Assert.NotNull(plan);
        Assert.Equal("dotnet", plan!.Tool);
        Assert.Equal(">=10", plan.Version);
        Assert.EndsWith(Path.Combine("tools", "dotnet"), plan.Directory);

        // Inside QuickRun's own folder: nothing here may install into the system.
        Assert.StartsWith(Path.Combine("C:", "quickrun", "tools"), plan.Directory);
    }

    [Fact]
    public void A_requirement_the_machine_already_meets_installs_nothing()
    {
        var satisfied = new ToolCheckResult(
            new ToolRequirement("dotnet", ">=10", null, Optional: false), true, "10.0.100", true);

        Assert.Null(Provisioner.PlanFor(satisfied, "tools"));
    }

    /// <summary>
    /// Optional means the run works without it. Downloading a toolchain for a requirement that was
    /// never going to stop anything is not helpfulness, it is a hundred megabytes nobody asked for.
    /// </summary>
    [Fact]
    public void An_optional_requirement_installs_nothing()
    {
        Assert.Null(Provisioner.PlanFor(Missing("dotnet", ">=10", optional: true), "tools"));
    }

    [Fact]
    public void A_tool_it_cannot_install_stays_the_users_problem()
    {
        Assert.Null(Provisioner.PlanFor(Missing("docker", null), "tools"));
    }
}
