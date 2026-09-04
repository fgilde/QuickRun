using QuickRun.App.Ui;

namespace QuickRun.App.Tests;

/// <summary>
/// The address the desktop window shows the page at.
/// <para>
/// Reported as "the QuickRun window opens but the repository is not loaded, and only a new browser
/// tab works". The window was created pointing at the plain page and then navigated to the target a
/// moment later - and a brand-new window's view is still starting at that moment, so the navigation
/// was dropped and the page loaded with nothing to do. The second press found a window that had
/// finished loading and worked, which is what made it look intermittent.
/// </para>
/// <para>
/// So the target belongs in the address the view is created with. That is what these hold: it is one
/// function, both callers use it, and the fragment survives it.
/// </para>
/// </summary>
public class WindowTargetTests
{
    private const string Listener = "http://127.0.0.1:9876";

    [Fact]
    public void The_page_knows_it_is_in_the_window()
    {
        // The page offers a way out into the real browser when it is inside the window, and that is
        // the only thing this flag decides.
        Assert.Equal($"{Listener}/?shell=window", DashboardWindow.PageUrl(Listener));
    }

    [Fact]
    public void A_target_rides_along_in_the_address()
    {
        var url = DashboardWindow.PageUrl(Listener, "#run?repo=acme%2Fapp&ref=main");

        // The whole of the fix: a window created with this address loads the target the way a fresh
        // browser tab does - which is the case that always worked.
        Assert.Equal($"{Listener}/?shell=window#run?repo=acme%2Fapp&ref=main", url);

        // The fragment comes last, or the query would swallow it and the page would see neither.
        Assert.EndsWith("#run?repo=acme%2Fapp&ref=main", url);
        Assert.Contains("?shell=window#", url);
    }

    /// <summary>
    /// A window can be created already pointing at a target.
    /// <para>
    /// The URL tests above cannot see the mistake that caused this - the address was right, it was
    /// simply applied too late. This can: without a constructor that takes the target, there is no
    /// way to create a window pointing anywhere, and the only option left is the navigation
    /// afterwards that the view drops while it is starting.
    /// </para>
    /// </summary>
    [Fact]
    public void A_window_is_created_pointing_at_its_target()
    {
        var accepts = typeof(DashboardWindow).GetConstructors()
            .Any(c => c.GetParameters() is [_, _, { ParameterType.Name: "String" },
                { ParameterType.Name: "String" }]);

        Assert.True(accepts,
            "DashboardWindow no longer takes a target - a new window can only be navigated after "
            + "it exists, which is the race this fixed");
    }

    /// <summary>
    /// Every kind of target a hand-over carries, unchanged.
    /// <para>
    /// These are built by <c>RunTarget</c>, which has already checked and escaped them; this must
    /// not be a second place that reinterprets one.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("#run?repo=acme%2Fapp")]
    [InlineData("#run?repo=acme%2Fapp&pr=42")]
    [InlineData("#run?repo=acme%2Fapp&config=collection")]
    [InlineData("#run?file=C%3A%2Fdev%2Fquickrun.yml")]
    [InlineData("#run?id=a1b2c3d4e5f6")]
    [InlineData("#builder")]
    public void A_target_is_carried_and_not_reinterpreted(string hash)
    {
        Assert.EndsWith(hash, DashboardWindow.PageUrl(Listener, hash));
    }
}
