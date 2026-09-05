using QuickRun.Core.Config;

namespace QuickRun.Core.Tests;

/// <summary>
/// Looking a repository up in QuickRun's collection.
/// <para>
/// Two things have to hold. It must never turn a repository name into a request for something else -
/// the name goes into a URL and into a file name, and it is the caller's string. And it must be
/// switchable off, because asking tells quickrun.org which repository somebody is starting, which is
/// not a thing to do to people without saying so.
/// </para>
/// </summary>
public class ConfigCollectionTests : IDisposable
{
    private readonly string _cache = Directory.CreateTempSubdirectory("quickrun-collection").FullName;

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(ConfigCollection.OptOut, null);
        try { Directory.Delete(_cache, recursive: true); } catch (IOException) { }
    }

    [Theory]
    [InlineData("acme/app", "acme/app")]
    [InlineData("https://github.com/acme/app", "acme/app")]
    [InlineData("https://github.com/acme/app.git", "acme/app")]
    [InlineData("git@github.com:acme/app.git", "acme/app")]
    [InlineData("ssh://git@github.com/acme/app", "acme/app")]
    [InlineData("open-webui/open-webui", "open-webui/open-webui")]
    public void Every_shape_of_repository_reduces_to_owner_and_repo(string repo, string expected)
    {
        Assert.Equal(expected, ConfigCollection.RepoPath(repo));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("acme")]
    [InlineData("acme/app/extra")]
    [InlineData("../../etc/passwd")]
    [InlineData("acme/../../secrets")]
    [InlineData("acme/app?x=1")]
    [InlineData("acme/ap p")]
    [InlineData("https://evil.example.com/acme/app")]
    [InlineData("C:/dev/secrets")]
    [InlineData("/etc/passwd")]
    public void Anything_that_is_not_a_plain_repository_name_is_refused(string repo)
    {
        // Refused rather than sanitised: a name this cannot make sense of has no config, and
        // guessing what somebody meant is how a lookup becomes a way to fetch other things.
        Assert.Null(ConfigCollection.RepoPath(repo));
    }

    [Fact]
    public void A_found_config_is_returned_and_cached()
    {
        var calls = new List<string>();

        var first = ConfigCollection.For("acme/app", _cache, url =>
        {
            calls.Add(url);
            return "name: From the collection\ntasks: [{run: echo hi}]\n";
        });

        Assert.Contains("From the collection", first);
        Assert.Equal(new[] { "https://quickrun.org/configs/acme/app.yml" }, calls);

        // The second run does not ask again: a config nobody changed is not worth a request, and a
        // run must not wait on the network for something it already has.
        var second = ConfigCollection.For("acme/app", _cache, url =>
        {
            calls.Add(url);
            return "name: Asked again\ntasks: []\n";
        });

        Assert.Contains("From the collection", second);
        Assert.Single(calls);
    }

    [Fact]
    public void Nothing_kept_for_this_repository_is_not_an_error()
    {
        Assert.Null(ConfigCollection.For("acme/app", _cache, _ => null));
    }

    [Fact]
    public void A_stale_answer_beats_no_answer_when_the_network_is_gone()
    {
        ConfigCollection.For("acme/app", _cache, _ => "name: Cached\ntasks: []\n");

        // Older than the cache window, and the network unreachable: what is on disk was still
        // written for this repository, which beats guessing from file names.
        var cached = Directory.EnumerateFiles(_cache).Single();
        File.SetLastWriteTimeUtc(cached, DateTime.UtcNow - ConfigCollection.TrustFor - TimeSpan.FromHours(1));

        Assert.Contains("Cached", ConfigCollection.For("acme/app", _cache, _ => null));
    }

    /// <summary>
    /// A corrected config reaches the next run, not the next day.
    /// <para>
    /// This is the one that bit: passbolt was fixed, deployed, and the run after it still started
    /// the broken copy out of this cache, because a cached answer counted as current for a day. A
    /// config nobody can correct within a day is a config that cannot be corrected.
    /// </para>
    /// </summary>
    [Fact]
    public void A_config_that_changed_is_picked_up_rather_than_waited_out()
    {
        ConfigCollection.For("acme/app", _cache, _ => "name: The old one\ntasks: []\n");

        // Half an hour, written out rather than derived from the window: a test that ages the copy
        // by "the window plus a bit" moves with the window and would pass at a day just as happily,
        // which is the setting that caused this. Half an hour is the claim - a config corrected
        // while somebody was at lunch is the one their next run starts.
        var cached = Directory.EnumerateFiles(_cache).Single();
        File.SetLastWriteTimeUtc(cached, DateTime.UtcNow - TimeSpan.FromMinutes(30));

        var answer = ConfigCollection.For("acme/app", _cache, _ => "name: The corrected one\ntasks: []\n");

        Assert.Contains("The corrected one", answer);

        // And it is what the next run starts from, without asking again.
        Assert.Contains("The corrected one", ConfigCollection.For("acme/app", _cache, _ => null));
    }

    /// <summary>
    /// Within the window nothing is asked, which is what keeps a run off the network.
    /// </summary>
    [Fact]
    public void A_fresh_copy_is_used_without_asking()
    {
        ConfigCollection.For("acme/app", _cache, _ => "name: Cached\ntasks: []\n");

        var asked = false;
        var answer = ConfigCollection.For("acme/app", _cache, _ => { asked = true; return "name: Other"; });

        Assert.Contains("Cached", answer);
        Assert.False(asked, "it went to the network for a copy it had just written");
    }

    [Fact]
    public void Switched_off_means_nothing_is_asked_at_all()
    {
        Environment.SetEnvironmentVariable(ConfigCollection.OptOut, "1");

        var asked = false;
        var answer = ConfigCollection.For("acme/app", _cache, _ => { asked = true; return "name: x"; });

        Assert.Null(answer);
        Assert.False(asked, "it asked despite being switched off");
    }

    [Fact]
    public void A_refused_name_is_never_requested()
    {
        var asked = false;

        ConfigCollection.For("../../etc/passwd", _cache, _ => { asked = true; return "name: x"; });

        Assert.False(asked, "a name it could not make sense of was still turned into a request");
    }
}
