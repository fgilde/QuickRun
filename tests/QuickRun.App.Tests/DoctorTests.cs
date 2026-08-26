using QuickRun.App.Commands;

namespace QuickRun.App.Tests;

/// <summary>
/// The self-check, checked. Its whole purpose is to be believed when it says a machine is fine, so
/// the checks that are not about this machine's desktop run here on every build.
/// </summary>
public class DoctorTests
{
    [Fact]
    public async Task Nothing_that_matters_is_broken_on_the_machine_running_the_tests()
    {
        var findings = await DoctorCommand.HeadlessAsync();

        var broken = findings.Where(f => !f.Ok && f.Fatal).Select(f => $"{f.What}: {f.Detail}");
        Assert.Empty(broken);
    }

    /// <summary>
    /// The confirmation gate as the outside sees it. A page must not be able to start a run, and an
    /// extension must be - the same boundary the daemon's own tests cover from the inside, checked
    /// here through the command a user can run themselves.
    /// </summary>
    [Fact]
    public async Task The_run_endpoint_refuses_a_page_and_accepts_an_extension()
    {
        var findings = await DoctorCommand.HeadlessAsync();

        var refused = Assert.Single(findings, f => f.What == "a page may not start a run");
        var accepted = Assert.Single(findings, f => f.What == "an extension may start a run");

        Assert.True(refused.Ok, refused.Detail);
        Assert.True(accepted.Ok, accepted.Detail);
    }
}
