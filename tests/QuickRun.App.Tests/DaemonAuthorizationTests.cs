using Microsoft.AspNetCore.Http;
using QuickRun.App.Daemon;

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
