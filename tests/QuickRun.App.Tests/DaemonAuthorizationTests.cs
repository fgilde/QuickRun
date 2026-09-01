using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using QuickRun.App.Commands;
using QuickRun.App.Daemon;
using QuickRun.Core.Config;

namespace QuickRun.App.Tests;

/// <summary>
/// The rule that replaced pairing.
/// <para>
/// A browser sets Origin itself on every cross-origin request and a page cannot change it, so it
/// is the only claim about a caller that can be trusted. These tests are the security boundary:
/// if https://github.com ever becomes acceptable again, any script on that site can start
/// arbitrary code on the machine.
/// </para>
/// </summary>
public class DaemonAuthorizationTests
{
    private static HttpContext From(string? origin)
    {
        var context = new DefaultHttpContext();
        if (origin is not null) context.Request.Headers.Origin = origin;
        return context;
    }

    /// <summary>
    /// A trusted web site may ask for the window, and gets nothing else with it.
    /// <para>
    /// This is the one place a page reaches at all, and what it reaches does exactly one thing: open
    /// QuickRun's own window on a plan that then waits for a person. Everything that could start,
    /// stop or read a run stays behind Authorized(), which the same page still cannot pass - so the
    /// worst a trusted site can do is make a window appear.
    /// </para>
    /// </summary>
    [Fact]
    public void ATrustedSiteMayAskForTheWindowAndNothingElse()
    {
        var root = Directory.CreateTempSubdirectory("quickrun-trust-daemon").FullName;
        try
        {
            var trusted = new TrustedSites(root);
            var page = From("https://quickrun.org");

            Assert.True(DaemonHost.FromTrustedSite(page, trusted));

            // And it is still a web page as far as every other endpoint is concerned.
            Assert.False(DaemonHost.Authorized(page));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Theory]
    [InlineData("https://github.com")]
    [InlineData("https://notquickrun.org")]
    [InlineData("https://quickrun.org.attacker.example")]
    [InlineData("http://quickrun.org")]
    public void AnUntrustedSiteMayNotEvenAskForTheWindow(string origin)
    {
        var root = Directory.CreateTempSubdirectory("quickrun-trust-deny").FullName;
        try
        {
            Assert.False(DaemonHost.FromTrustedSite(From(origin), new TrustedSites(root)));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// What the extension may name and a web page may not: a file on this machine.
    /// <para>
    /// A quickrun:// link carrying a config path is somebody clicking a link they were given, and it
    /// is labelled as such in the window. A page reaching the daemon over HTTP and naming a path on
    /// the reader's disk is the thing the origin rules exist to prevent, and trusting a site with a
    /// window is not trusting it with that.
    /// </para>
    /// </summary>
    [Fact]
    public void AWebPageMayNotNameAFileOnThisMachine()
    {
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["file"] = "C:/dev/secrets/quickrun.yml",
        });

        Assert.NotNull(RunTarget.FromQuery(query, allowFile: true));
        Assert.Null(RunTarget.FromQuery(query, allowFile: false));
    }

    /// <summary>
    /// Which config was asked for travels with the target, because it changes what runs.
    /// <para>
    /// A name, never a config: "the one QuickRun keeps for this repository", which QuickRun fetches
    /// itself. Dropped on the way, a press of "run it with our config" would quietly open the window
    /// on the repository's own - a button saying one thing and doing another.
    /// </para>
    /// </summary>
    [Fact]
    public void TheCollectionChoiceSurvivesTheHandover()
    {
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["repo"] = "acme/app",
            ["config"] = "collection",
        });

        Assert.Contains("config=collection", RunTarget.FromQuery(query));

        // And nothing else is taken from that field - it is a name, not a path or a config.
        var other = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["repo"] = "acme/app",
            ["config"] = "../../etc/passwd",
        });

        Assert.DoesNotContain("config", RunTarget.FromQuery(other));
    }

    [Theory]
    [InlineData("chrome-extension://gemnfgcfaacphpmbaipjejmohdkhkfda")]
    [InlineData("moz-extension://4f2c8a1e-0000-0000-0000-000000000000")]
    [InlineData("safari-web-extension://ABCDEF01-2345-6789-ABCD-EF0123456789")]
    public void BrowserExtensionsMayStartRuns(string origin) =>
        Assert.True(DaemonHost.Authorized(From(origin)));

    [Theory]
    [InlineData("https://github.com")]
    [InlineData("https://github.com.evil.example")]
    [InlineData("https://evil.example")]
    [InlineData("http://127.0.0.1:9876")]
    [InlineData("null")]
    public void WebPagesMayNot(string origin) =>
        Assert.False(DaemonHost.Authorized(From(origin)));

    [Fact]
    public void AnOriginThatMerelyMentionsAnExtensionSchemeIsRejected() =>
        Assert.False(DaemonHost.Authorized(From("https://evil.example/chrome-extension://x")));

    [Fact]
    public void ALocalProgramWithoutAnOriginIsAllowed()
    {
        // curl and QuickRun's own CLI send no Origin. Such a caller already runs with the user's
        // privileges, so the daemon grants it nothing it did not have.
        Assert.True(DaemonHost.Authorized(From(null)));
        Assert.True(DaemonHost.Authorized(From("")));
    }

    /// <summary>
    /// A repository is somewhere else. Running a folder on this machine is something you ask for by
    /// pointing at it - a shell verb, or the command line - and never something a page can request
    /// through the extension, however the path is spelled.
    /// </summary>
    [Theory]
    [InlineData("file:///C:/dev/secrets")]
    [InlineData("file:///etc")]
    [InlineData("C:\\\\dev\\\\secrets")]
    [InlineData("c:/dev/secrets")]
    [InlineData("/etc/passwd")]
    [InlineData("~/projects/mine")]
    [InlineData("\\\\\\\\server\\\\share")]
    public void ARepositoryThatPointsAtThisMachineIsRefused(string repo) =>
        Assert.True(DaemonHost.PointsAtThisMachine(repo), repo);

    [Theory]
    [InlineData("acme/app")]
    [InlineData("https://github.com/acme/app")]
    [InlineData("git@github.com:acme/app.git")]
    [InlineData("ssh://git@example.com/acme/app")]
    public void AnOrdinaryRepositoryIsNotMistakenForOne(string repo) =>
        Assert.False(DaemonHost.PointsAtThisMachine(repo), repo);

    /// <summary>
    /// A config named over HTTP is a stranger's string, and it decides which file QuickRun opens -
    /// a parse failure then quotes what it found. So only a file inside the checkout counts.
    /// </summary>
    [Theory]
    [InlineData("quickrun.yml")]
    [InlineData("ci/quickrun.yml")]
    [InlineData("deploy/other.yaml")]
    [InlineData("a/b/c/run.YML")]
    public void AConfigInsideTheRepositoryIsAccepted(string config) =>
        Assert.True(DaemonHost.ConfigInsideRepository(config), config);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/etc/passwd")]
    [InlineData("/etc/shadow.yml")]
    [InlineData("C:/Windows/win.yml")]
    [InlineData(@"c:\windows\win.yaml")]
    [InlineData("~/secrets.yml")]
    [InlineData("../outside.yml")]
    [InlineData("ci/../../outside.yml")]
    [InlineData("./quickrun.yml")]
    [InlineData(@"\\server\share\run.yml")]
    [InlineData("https://evil.example.com/run.yml")]
    [InlineData("quickrun.txt")]
    [InlineData("quickrun.yml.exe")]
    [InlineData("run.yml.txt")]
    public void AnythingElseIsRefused(string config) =>
        Assert.False(DaemonHost.ConfigInsideRepository(config), config);

    [Fact]
    public void AVeryLongNameIsRefused() =>
        Assert.False(DaemonHost.ConfigInsideRepository(new string('a', 300) + ".yml"));

    /// <summary>
    /// A NUL ends a string for the operating system while .NET carries on past it, so "run.yml\0.exe"
    /// is one name to the check and another to the file system. Newlines have no business in a file
    /// name either.
    /// </summary>
    [Fact]
    public void AControlCharacterIsRefused()
    {
        Assert.False(DaemonHost.ConfigInsideRepository("run.yml\0.exe"));
        Assert.False(DaemonHost.ConfigInsideRepository("run\n.yml"));
        Assert.False(DaemonHost.ConfigInsideRepository("run\t.yml"));
    }
}
