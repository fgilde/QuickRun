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

    [Fact]
    public void The_page_explains_how_to_pair()
    {
        var html = new Dashboard().Render(9876);

        Assert.Contains("pairing window", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("quickrun-extension-chromium.zip", html);
        Assert.Contains("quickrun-extension-firefox.zip", html);
    }
}
