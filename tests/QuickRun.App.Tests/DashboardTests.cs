using QuickRun.App.Daemon;

namespace QuickRun.App.Tests;

public class DashboardTests
{
    [Fact]
    public void The_page_carries_its_token_and_port()
    {
        var dashboard = new Dashboard();
        var html = dashboard.Render(9876);

        Assert.Contains(dashboard.Token, html);
        Assert.Contains("9876", html);
        Assert.DoesNotContain("{{TOKEN}}", html);
        Assert.DoesNotContain("{{PORT}}", html);
        Assert.DoesNotContain("{{VERSION}}", html);
    }

    [Fact]
    public void The_token_is_long_and_hex()
    {
        var token = new Dashboard().Token;

        Assert.Equal(48, token.Length);
        Assert.Matches("^[0-9a-f]+$", token);
    }

    [Fact]
    public void Each_dashboard_gets_its_own_token()
        => Assert.NotEqual(new Dashboard().Token, new Dashboard().Token);

    /// <summary>
    /// The reason this token exists: CORS stops another origin reading a response but not sending
    /// the request, so a page could otherwise POST to the dashboard's endpoints.
    /// </summary>
    [Fact]
    public void Only_the_page_own_token_is_accepted()
    {
        var dashboard = new Dashboard();

        Assert.True(dashboard.Authorized(dashboard.Token));
        Assert.False(dashboard.Authorized(null));
        Assert.False(dashboard.Authorized(""));
        Assert.False(dashboard.Authorized("wrong"));
        Assert.False(dashboard.Authorized(dashboard.Token + "a"));
        Assert.False(dashboard.Authorized(new Dashboard().Token));
    }

    [Fact]
    public void The_page_renders_the_sections_a_user_needs()
    {
        var html = new Dashboard().Render(9876);

        foreach (var expected in new[] { "Runs", "Workspaces", "Browser extension", "About" })
            Assert.Contains(expected, html);
    }

    /// <summary>
    /// Pairing is gone, so the page must not still promise it - and what replaced it, registering
    /// the scheme the extension falls back to, has to be reachable from here.
    /// </summary>
    [Fact]
    public void The_page_offers_the_protocol_registration_and_no_pairing()
    {
        var html = new Dashboard().Render(9876);

        Assert.Contains("quickrun://", html);
        Assert.Contains("schemeButton", html);
        Assert.DoesNotContain("pairing window", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://fgilde.github.io/QuickRun/download", html);
    }

    /// <summary>
    /// A stopped run has to read as stopped, and what the repository says it is belongs next to it.
    /// </summary>
    [Fact]
    public void The_page_renders_a_description_and_a_stopped_run()
    {
        var html = new Dashboard().Render(9876);

        Assert.Contains("run.description", html);
        Assert.Contains("cancelled: ['stopped', 'warn']", html);
        Assert.Contains("Stopping", html);
    }

    /// <summary>
    /// The form a config declares has to be on the page, and built as text: a label or an option
    /// comes out of someone's repository.
    /// </summary>
    [Fact]
    public void The_page_can_ask_for_a_configs_inputs()
    {
        var html = new Dashboard().Render(9876);

        Assert.Contains("function inputForm(run)", html);
        Assert.Contains("/inputs`", html);
        Assert.Contains("awaitingInput", html);

        // A secret must not be put back into a field.
        Assert.Contains("def.type === 'password' ? '' :", html);
        Assert.DoesNotContain("innerHTML = def.label", html);
    }

    /// <summary>A run can be started from this page, which is what makes the extension optional.</summary>
    [Fact]
    public void The_page_can_start_a_run_itself()
    {
        var html = new Dashboard().Render(9876);

        Assert.Contains("repoInput", html);
        Assert.Contains("refSelect", html);
        Assert.Contains("/api/dashboard/branches", html);
        Assert.Contains("/api/dashboard/run", html);

        // The gate: the page confirms separately, it does not start on prepare.
        Assert.Contains("/api/dashboard/runs/${run.id}/confirm", html);
    }

    /// <summary>
    /// Downloads always go through the project's own page, never a GitHub release page or a
    /// hand-written asset link that would rot when the assets change.
    /// </summary>
    [Fact]
    public void The_page_links_no_release_assets_directly()
    {
        var html = new Dashboard().Render(9876);

        Assert.DoesNotContain("releases/latest/download", html);
        Assert.DoesNotContain("releases/tag/", html);
    }
}
