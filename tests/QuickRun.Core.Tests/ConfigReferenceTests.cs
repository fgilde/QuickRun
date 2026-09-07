using QuickRun.Core.Config;

namespace QuickRun.Core.Tests;

/// <summary>
/// Which config a link or a button may ask for.
/// <para>
/// This is a security boundary, and the interesting half is what it refuses. A reference names a
/// file that QuickRun fetches itself and shows in the window before anything runs; the two things
/// it must never become are commands out of a link and a way to make the daemon reach into the
/// network it is sitting in.
/// </para>
/// </summary>
public class ConfigReferenceTests
{
    [Fact]
    public void The_keyword_asks_for_the_config_quickrun_keeps()
    {
        Assert.Equal(ConfigReferenceKind.Collection, ConfigReference.Read("collection").Kind);
        Assert.Equal(ConfigReferenceKind.Collection, ConfigReference.Read("Collection").Kind);
    }

    [Theory]
    [InlineData("quickrun.yml")]
    [InlineData("samples/server/quickrun.yml")]
    [InlineData("docs/run.yaml")]
    public void A_path_inside_the_repository_is_a_reference(string path)
    {
        var reference = ConfigReference.Read(path);

        Assert.Equal(ConfigReferenceKind.InsideRepository, reference.Kind);
        Assert.Equal(path, reference.Value);
    }

    /// <summary>
    /// Nothing that leaves the repository, and nothing that names this machine.
    /// </summary>
    [Theory]
    [InlineData("../../etc/passwd.yml")]
    [InlineData("/etc/quickrun.yml")]
    [InlineData("C:/dev/secrets/quickrun.yml")]
    [InlineData("\\\\server\\share\\quickrun.yml")]
    [InlineData("./quickrun.yml")]
    [InlineData("samples//quickrun.yml")]
    [InlineData("file:///C:/dev/quickrun.yml")]
    // A home-relative path is a path on the reader's machine, whatever a shell would expand it to.
    [InlineData("~/secrets.yml")]
    [InlineData("~\\secrets.yml")]
    public void A_path_out_of_the_repository_is_refused(string path)
        => Assert.False(ConfigReference.Read(path).Usable);

    /// <summary>
    /// An address may be long; a name inside a repository has no reason to be. Both were capped at
    /// one length when these rules moved into one place, which made the second one laxer than the
    /// rule it replaced - caught by the tests that describe the endpoint.
    /// </summary>
    [Fact]
    public void A_name_far_longer_than_a_name_is_refused()
    {
        Assert.False(ConfigReference.Read(new string('a', 300) + ".yml").Usable);

        // The same length is fine in an address, where a query string can account for it.
        var long_ = $"https://example.com/{new string('a', 300)}.yml";
        Assert.Equal(ConfigReferenceKind.Remote, ConfigReference.Read(long_).Kind);
    }

    [Theory]
    [InlineData("quickrun.txt")]
    [InlineData("samples/quickrun")]
    [InlineData("README.md")]
    public void Only_a_config_file_is_a_reference(string path)
        => Assert.False(ConfigReference.Read(path).Usable);

    [Fact]
    public void A_published_config_is_a_reference()
    {
        var reference = ConfigReference.Read("https://example.com/configs/app.yml");

        Assert.Equal(ConfigReferenceKind.Remote, reference.Kind);
        Assert.Equal("https://example.com/configs/app.yml", reference.Value);
    }

    /// <summary>
    /// http is not enough for something the daemon will fetch and then offer to run: anything on
    /// the way could replace it, and the address in the link would still look right.
    /// </summary>
    [Theory]
    [InlineData("http://example.com/app.yml")]
    [InlineData("ftp://example.com/app.yml")]
    [InlineData("https://user:secret@example.com/app.yml")]
    [InlineData("https://example.com/app.txt")]
    [InlineData("https://example.com/")]
    public void A_published_config_has_to_be_https_and_a_config(string url)
        => Assert.False(ConfigReference.Read(url).Usable);

    /// <summary>
    /// The daemon must not be turned into a way of asking what answers inside a network.
    /// <para>
    /// The plan is still shown and still has to be approved, so a config from elsewhere cannot run
    /// anything by itself - but the fetch is a request made from inside somebody's network, and a
    /// page choosing its address would be a scanner. That is not a config feature.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("https://localhost/app.yml")]
    [InlineData("https://127.0.0.1/app.yml")]
    [InlineData("https://[::1]/app.yml")]
    [InlineData("https://10.1.2.3/app.yml")]
    [InlineData("https://192.168.0.5/app.yml")]
    [InlineData("https://172.20.0.1/app.yml")]
    [InlineData("https://169.254.169.254/app.yml")]
    [InlineData("https://intranet/app.yml")]
    [InlineData("https://printer.local/app.yml")]
    [InlineData("https://api.internal/app.yml")]
    public void An_address_on_this_machine_or_its_network_is_refused(string url)
        => Assert.False(ConfigReference.Read(url).Usable);

    [Theory]
    [InlineData("8.8.8.8", false)]
    [InlineData("172.15.0.1", false)]
    [InlineData("172.32.0.1", false)]
    [InlineData("example.com", false)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("fc00::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("2606:4700::1111", false)]
    public void The_edges_of_the_private_ranges_are_where_they_should_be(string host, bool reachable)
        => Assert.Equal(reachable, ConfigReference.Reachable(host));

    [Fact]
    public void Nothing_and_nonsense_are_not_references()
    {
        Assert.False(ConfigReference.Read(null).Usable);
        Assert.False(ConfigReference.Read("").Usable);
        Assert.False(ConfigReference.Read("   ").Usable);

        // A name long enough to be a payload rather than a name.
        Assert.False(ConfigReference.Read(new string('a', 500) + ".yml").Usable);

        // A control character is how a value gets smuggled past something that logs it.
        Assert.False(ConfigReference.Read("samples/\nquickrun.yml").Usable);
    }
}
