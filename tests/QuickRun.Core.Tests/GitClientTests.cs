using QuickRun.Core.Git;
using QuickRun.Core.Process;

namespace QuickRun.Core.Tests;

public class GitClientTests
{
    /// <summary>A client whose credential chain finds nothing, so tests never read real credentials.</summary>
    private static GitClient Client(string? token = null) =>
        new(new CredentialResolver(token, (_, _) => new CommandResult(1, "", false), _ => null));

    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "quickrun-out-" + Guid.NewGuid().ToString("n")[..8]);

    [Theory]
    [InlineData("acme/app", "https://github.com/acme/app")]
    [InlineData("github.com/acme/app", "https://github.com/acme/app")]
    [InlineData("https://github.com/acme/app", "https://github.com/acme/app")]
    [InlineData("https://github.com/acme/app.git", "https://github.com/acme/app.git")]
    [InlineData("git@github.com:acme/app.git", "git@github.com:acme/app.git")]
    public void NormalizeRepoUrl_accepts_the_documented_shapes(string input, string expected)
        => Assert.Equal(expected, GitClient.NormalizeRepoUrl(input));

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("not a url at all")]
    [InlineData("")]
    [InlineData("acme")]
    public void NormalizeRepoUrl_rejects_anything_else(string input)
        => Assert.Throws<ArgumentException>(() => GitClient.NormalizeRepoUrl(input));

    [Fact]
    public void AuthUrl_injects_a_token_into_an_https_url()
        => Assert.Equal("https://ghp_x@github.com/acme/app",
            GitClient.AuthUrl("https://github.com/acme/app", "ghp_x"));

    [Fact]
    public void AuthUrl_leaves_ssh_urls_and_null_tokens_alone()
    {
        Assert.Equal("git@github.com:acme/app.git", GitClient.AuthUrl("git@github.com:acme/app.git", "ghp_x"));
        Assert.Equal("https://github.com/acme/app", GitClient.AuthUrl("https://github.com/acme/app", null));
    }

    [Fact]
    public void AuthUrl_does_not_add_a_second_credential()
        => Assert.Equal("https://someone@github.com/acme/app",
            GitClient.AuthUrl("https://someone@github.com/acme/app", "ghp_x"));

    [Fact]
    public void Scrub_removes_the_token_in_plain_and_url_encoded_form()
    {
        // A token with characters AuthUrl actually escapes, so both forms can appear in git output.
        const string token = "ghp/secret+x";
        var encoded = Uri.EscapeDataString(token);
        var scrubbed = GitClient.Scrub($"failed for {token} and {encoded}", token);

        Assert.DoesNotContain(token, scrubbed);
        Assert.DoesNotContain(encoded, scrubbed);
    }

    [Fact]
    public void HostOf_extracts_the_host_from_both_url_forms()
    {
        Assert.Equal("github.com", GitClient.HostOf("https://github.com/acme/app"));
        Assert.Equal("github.com", GitClient.HostOf("git@github.com:acme/app.git"));
    }

    [Fact]
    public void CheckoutOrUpdate_clones_a_local_repository()
    {
        using var repo = new LocalRepo();
        var target = TempDir();
        try
        {
            var outcome = Client().CheckoutOrUpdate(repo.Url, "main", null, target, fresh: false);
            Assert.True(outcome.Ok, outcome.Error);
            Assert.True(File.Exists(Path.Combine(target, "README.md")));
            Assert.Equal(repo.Head(), outcome.Commit);
        }
        finally { LocalRepo.DeleteTree(target); }
    }

    [Fact]
    public void CheckoutOrUpdate_checks_out_the_requested_branch()
    {
        using var repo = new LocalRepo();
        repo.Branch("feature/login");
        repo.Write("feature.txt", "x");
        repo.Commit("feature");
        repo.Checkout("main");

        var target = TempDir();
        try
        {
            var outcome = Client().CheckoutOrUpdate(repo.Url, "feature/login", null, target, false);
            Assert.True(outcome.Ok, outcome.Error);
            Assert.True(File.Exists(Path.Combine(target, "feature.txt")));
        }
        finally { LocalRepo.DeleteTree(target); }
    }

    [Fact]
    public void CheckoutOrUpdate_checks_out_a_tag()
    {
        using var repo = new LocalRepo();
        repo.Tag("v1.0");

        var target = TempDir();
        try
        {
            Assert.True(Client().CheckoutOrUpdate(repo.Url, "v1.0", null, target, false).Ok);
            Assert.True(File.Exists(Path.Combine(target, "README.md")));
        }
        finally { LocalRepo.DeleteTree(target); }
    }

    [Fact]
    public void CheckoutOrUpdate_updates_an_existing_workspace_to_the_new_head()
    {
        using var repo = new LocalRepo();
        var target = TempDir();
        try
        {
            Assert.True(Client().CheckoutOrUpdate(repo.Url, "main", null, target, false).Ok);

            repo.Write("second.txt", "x");
            repo.Commit("second");

            var outcome = Client().CheckoutOrUpdate(repo.Url, "main", null, target, false);
            Assert.True(outcome.Ok, outcome.Error);
            Assert.True(File.Exists(Path.Combine(target, "second.txt")));
            Assert.Equal(repo.Head(), outcome.Commit);
        }
        finally { LocalRepo.DeleteTree(target); }
    }

    [Fact]
    public void CheckoutOrUpdate_discards_local_modifications_on_update()
    {
        using var repo = new LocalRepo();
        var target = TempDir();
        try
        {
            Client().CheckoutOrUpdate(repo.Url, "main", null, target, false);
            File.WriteAllText(Path.Combine(target, "README.md"), "vandalised");

            Client().CheckoutOrUpdate(repo.Url, "main", null, target, false);
            Assert.Equal("hello", File.ReadAllText(Path.Combine(target, "README.md")));
        }
        finally { LocalRepo.DeleteTree(target); }
    }

    [Fact]
    public void CheckoutOrUpdate_keeps_dependency_caches_across_updates()
    {
        using var repo = new LocalRepo();
        var target = TempDir();
        try
        {
            Client().CheckoutOrUpdate(repo.Url, "main", null, target, false);

            var cache = Path.Combine(target, "node_modules", "left-pad");
            Directory.CreateDirectory(cache);
            File.WriteAllText(Path.Combine(cache, "index.js"), "x");

            Client().CheckoutOrUpdate(repo.Url, "main", null, target, false);
            Assert.True(File.Exists(Path.Combine(cache, "index.js")));
        }
        finally { LocalRepo.DeleteTree(target); }
    }

    [Fact]
    public void CheckoutOrUpdate_with_fresh_true_replaces_the_directory()
    {
        using var repo = new LocalRepo();
        var target = TempDir();
        try
        {
            Client().CheckoutOrUpdate(repo.Url, "main", null, target, false);
            File.WriteAllText(Path.Combine(target, "stray.txt"), "x");

            Assert.True(Client().CheckoutOrUpdate(repo.Url, "main", null, target, fresh: true).Ok);
            Assert.False(File.Exists(Path.Combine(target, "stray.txt")));
        }
        finally { LocalRepo.DeleteTree(target); }
    }

    [Fact]
    public void CheckoutOrUpdate_reports_an_unknown_ref_as_an_error()
    {
        using var repo = new LocalRepo();
        var target = TempDir();
        try
        {
            var outcome = Client().CheckoutOrUpdate(repo.Url, "no-such-branch", null, target, false);
            Assert.False(outcome.Ok);
            Assert.NotNull(outcome.Error);
        }
        finally { LocalRepo.DeleteTree(target); }
    }

    [Fact]
    public void CheckoutOrUpdate_does_not_leak_the_token_into_the_error()
    {
        var target = TempDir();
        try
        {
            var client = new GitClient(new CredentialResolver("ghp_supersecret",
                (_, _) => new CommandResult(1, "", false), _ => null));
            var outcome = client.CheckoutOrUpdate(
                "https://github.com/acme/definitely-not-real-8f2a", "main", null, target, false);

            Assert.False(outcome.Ok);
            Assert.DoesNotContain("ghp_supersecret", outcome.Error);
        }
        finally { LocalRepo.DeleteTree(target); }
    }

    [Fact]
    public void ListBranches_returns_the_branches_of_a_local_repository()
    {
        using var repo = new LocalRepo();
        repo.Branch("feature/login");
        repo.Write("f.txt", "x");
        repo.Commit("f");

        var (branches, error) = Client().ListBranches(repo.Url);

        Assert.Null(error);
        Assert.Contains("main", branches!);
        Assert.Contains("feature/login", branches!);
    }

    [Fact]
    public void HeadCommit_returns_null_outside_a_repository()
        => Assert.Null(Client().HeadCommit(Path.GetTempPath()));

    [Fact]
    public void An_explicit_token_wins_over_the_rest_of_the_chain()
        => Assert.Equal("explicit",
            new CredentialResolver("explicit", (_, _) => new CommandResult(0, "from-gh", false), _ => null)
                .Resolve("github.com"));

    [Fact]
    public void The_gh_cli_token_is_used_when_nothing_else_is_set()
        => Assert.Equal("from-gh",
            new CredentialResolver(null, (_, _) => new CommandResult(0, "from-gh\n", false))
                .Resolve("github.com"));

    [Fact]
    public void No_credential_source_yields_null()
        => Assert.Null(new CredentialResolver(null, (_, _) => new CommandResult(1, "", false), _ => null).Resolve("github.com"));
}
