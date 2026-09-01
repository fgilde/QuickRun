using QuickRun.Core.Config;

namespace QuickRun.Core.Tests;

/// <summary>
/// Which sites may ask QuickRun to open its window.
/// <para>
/// This is a security boundary, and the interesting half is everything it says no to. A site on this
/// list still cannot start a run - it can ask for the window, and the plan waits there for a person -
/// but a matching rule that is too generous would hand that door to whoever registers
/// notquickrun.org.
/// </para>
/// </summary>
public class TrustedSitesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("quickrun-trusted").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private TrustedSites Sites() => new(_root);

    [Fact]
    public void The_site_quickrun_comes_from_is_trusted_to_begin_with()
    {
        var sites = Sites();

        Assert.True(sites.Trusts("https://quickrun.org"));
        Assert.True(sites.Trusts("https://www.quickrun.org"));

        // Whoever installed QuickRun from that page has already trusted it with more than a window.
        Assert.Equal(TrustedSites.Default, sites.Patterns);
    }

    /// <summary>
    /// The subdomain form matches whole labels, which is the whole point of writing it out.
    /// </summary>
    [Theory]
    [InlineData("quickrun.org", true)]
    [InlineData("www.quickrun.org", true)]
    [InlineData("a.b.quickrun.org", true)]
    [InlineData("QuickRun.ORG", true)]
    [InlineData("notquickrun.org", false)]
    [InlineData("quickrun.org.attacker.example", false)]
    [InlineData("quickrun-org.example", false)]
    [InlineData("xquickrun.org", false)]
    [InlineData("attacker.example", false)]
    public void A_subdomain_pattern_matches_labels_and_not_text(string host, bool expected) =>
        Assert.Equal(expected, TrustedSites.Matches("*.quickrun.org", host));

    [Theory]
    [InlineData("example.com", true)]
    [InlineData("www.example.com", false)]
    public void Without_a_star_only_that_host_matches(string host, bool expected) =>
        Assert.Equal(expected, TrustedSites.Matches("example.com", host));

    /// <summary>
    /// Plain http is not enough for a site that gets to reach into this machine.
    /// <para>
    /// Anything on the way can rewrite an http response, and the Origin header would still say the
    /// trusted name - so the name would be no evidence at all. Loopback is the exception: there is
    /// nothing in between to rewrite it.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("https://quickrun.org", true)]
    [InlineData("http://quickrun.org", false)]
    [InlineData("http://localhost:5173", true)]
    [InlineData("http://127.0.0.1:5173", true)]
    public void Only_https_counts_except_on_this_machine(string origin, bool expected)
    {
        var patterns = new[] { "*.quickrun.org", "localhost", "127.0.0.1" };

        Assert.Equal(expected, TrustedSites.Trusts(origin, patterns));
    }

    /// <summary>
    /// An Origin is a scheme, a host and a port. Anything else is not one and is not read as one.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("quickrun.org")]
    [InlineData("https://attacker.example/https://quickrun.org")]
    [InlineData("https://attacker.example/?x=quickrun.org")]
    [InlineData("https://attacker.example#quickrun.org")]
    [InlineData("chrome-extension://gemnfgcfaacphpmbaipjejmohdkhkfda")]
    [InlineData("file:///C:/x")]
    [InlineData("https://quickrun.org/page")]
    [InlineData("https://quickrun.org#x")]
    [InlineData("https://quickrun.org/?x=1")]
    public void Anything_that_is_not_a_web_origin_is_refused(string? origin) =>
        Assert.False(TrustedSites.Trusts(origin, new[] { "*.quickrun.org" }));

    [Fact]
    public void An_empty_list_trusts_nothing()
    {
        // Written out rather than absent: a file with no hosts in it is a decision, and the default
        // must not come back to override it.
        File.WriteAllText(Path.Combine(_root, TrustedSites.FileName), "# nothing here\n");

        var sites = Sites();

        Assert.Empty(sites.Patterns);
        Assert.False(sites.Trusts("https://quickrun.org"));
    }

    [Fact]
    public void What_the_user_adds_is_trusted_and_what_they_remove_is_not()
    {
        var sites = Sites();

        sites.Add("https://internal.example.com/some/page");
        Assert.True(sites.Trusts("https://internal.example.com"));

        // The default was written out alongside it rather than lost.
        Assert.True(sites.Trusts("https://quickrun.org"));

        sites.Remove("quickrun.org");
        Assert.False(sites.Trusts("https://quickrun.org"));
        Assert.True(sites.Trusts("https://internal.example.com"));
    }

    [Fact]
    public void Removing_everything_leaves_nothing_rather_than_the_default()
    {
        var sites = Sites();

        sites.Remove("*.quickrun.org");

        Assert.Empty(sites.Patterns);
        Assert.False(sites.Trusts("https://quickrun.org"));
    }

    [Theory]
    [InlineData("https://example.com/path?q=1", "example.com")]
    [InlineData("  Example.COM  ", "example.com")]
    [InlineData("example.com:8443", "example.com")]
    [InlineData("example.com/", "example.com")]
    [InlineData("*.example.com", "*.example.com")]
    [InlineData("hä?.example.com", "")]
    [InlineData("", "")]
    public void A_host_is_taken_out_of_whatever_was_typed(string typed, string expected) =>
        Assert.Equal(expected, TrustedSites.Normalise(typed));

    [Fact]
    public void The_file_says_what_it_is_for()
    {
        var sites = Sites();
        sites.Add("example.com");

        var text = File.ReadAllText(sites.Path);

        // Whoever opens this file has to be able to tell what listing a site here does and does not
        // allow, without going looking for documentation.
        Assert.Contains("cannot start a run", text);
        Assert.Contains("example.com", text);
    }
}
