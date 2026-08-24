using QuickRun.Core.Workspace;

namespace QuickRun.Core.Tests;

public class WorkspaceStoreTests
{
    private const string Repo = "https://github.com/acme/app";

    [Theory]
    [InlineData("https://github.com/acme/app", "main", "acme__app__main")]
    [InlineData("https://github.com/acme/app.git", "main", "acme__app__main")]
    [InlineData("git@github.com:acme/app.git", "main", "acme__app__main")]
    [InlineData("https://github.com/acme/app", "feature/login", "acme__app__feature__login")]
    public void IdFor_produces_a_readable_filesystem_safe_id(string repo, string @ref, string expected)
        => Assert.StartsWith(expected, WorkspaceStore.IdFor(repo, @ref));

    [Fact]
    public void IdFor_disambiguates_refs_that_sanitise_to_the_same_name()
        => Assert.NotEqual(WorkspaceStore.IdFor(Repo, "feature/login"), WorkspaceStore.IdFor(Repo, "feature__login"));

    [Fact]
    public void IdFor_is_stable_across_calls()
        => Assert.Equal(WorkspaceStore.IdFor(Repo, "main"), WorkspaceStore.IdFor(Repo, "main"));

    /// <summary>
    /// The stripped set is fixed, not the running platform's: the same repository and ref must
    /// produce the same id on Windows, Linux and macOS.
    /// </summary>
    [Fact]
    public void IdFor_strips_characters_that_are_illegal_on_any_supported_platform()
    {
        var id = WorkspaceStore.IdFor(Repo, "fix:colon?star*pipe|quote\"lt<gt>");

        foreach (var illegal in new[] { ':', '?', '*', '|', '"', '<', '>', '/', '\\' })
            Assert.DoesNotContain(illegal, id);
    }

    [Fact]
    public void IdFor_is_the_same_regardless_of_the_platform_it_runs_on()
    {
        // A literal, so a regression that reintroduces platform-dependent stripping is caught
        // wherever the suite runs rather than only on one operating system.
        Assert.StartsWith("acme__app__fix__colon", WorkspaceStore.IdFor(Repo, "fix:colon"));
    }

    [Fact]
    public void An_empty_store_lists_nothing()
    {
        using var home = new TempHome();
        Assert.Empty(new WorkspaceStore(home.Path).List());
    }

    [Fact]
    public void Touch_registers_a_workspace_that_List_then_returns()
    {
        using var home = new TempHome();
        var store = new WorkspaceStore(home.Path);
        var id = WorkspaceStore.IdFor(Repo, "main");
        Directory.CreateDirectory(store.PathFor(Repo, "main"));

        store.Touch(id, Repo, "main", "abc1234", true);

        var info = Assert.Single(store.List());
        Assert.Equal(id, info.Id);
        Assert.Equal("main", info.Ref);
        Assert.Equal(Repo, info.Repo);
        Assert.Equal("abc1234", info.LastCommit);
        Assert.True(info.LastOk);
    }

    [Fact]
    public void Touch_twice_updates_rather_than_duplicates()
    {
        using var home = new TempHome();
        var store = new WorkspaceStore(home.Path);
        var id = WorkspaceStore.IdFor(Repo, "main");
        Directory.CreateDirectory(store.PathFor(Repo, "main"));

        store.Touch(id, Repo, "main", "aaa", true);
        store.Touch(id, Repo, "main", "bbb", false);

        var info = Assert.Single(store.List());
        Assert.Equal("bbb", info.LastCommit);
        Assert.False(info.LastOk);
    }

    [Fact]
    public void List_reports_the_size_on_disk()
    {
        using var home = new TempHome();
        var store = new WorkspaceStore(home.Path);
        var path = store.PathFor(Repo, "main");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "big.txt"), new string('x', 5000));

        store.Touch(WorkspaceStore.IdFor(Repo, "main"), Repo, "main", null, null);

        Assert.True(Assert.Single(store.List()).Bytes >= 5000);
    }

    [Fact]
    public void Get_returns_a_registered_workspace_and_null_otherwise()
    {
        using var home = new TempHome();
        var store = new WorkspaceStore(home.Path);
        var id = WorkspaceStore.IdFor(Repo, "main");
        Directory.CreateDirectory(store.PathFor(Repo, "main"));
        store.Touch(id, Repo, "main", null, null);

        Assert.NotNull(store.Get(id));
        Assert.Null(store.Get("acme__other__main-000000"));
    }

    [Fact]
    public void Remove_deletes_the_directory_and_returns_true()
    {
        using var home = new TempHome();
        var store = new WorkspaceStore(home.Path);
        var path = store.PathFor(Repo, "main");
        Directory.CreateDirectory(path);
        var id = WorkspaceStore.IdFor(Repo, "main");
        store.Touch(id, Repo, "main", null, null);

        Assert.True(store.Remove(id));
        Assert.False(Directory.Exists(path));
        Assert.Empty(store.List());
    }

    [Fact]
    public void Remove_returns_false_for_an_unknown_id()
    {
        using var home = new TempHome();
        Assert.False(new WorkspaceStore(home.Path).Remove("acme__nothing__main-000000"));
    }

    [Theory]
    [InlineData("../../windows")]
    [InlineData("sub/dir")]
    [InlineData("..")]
    public void Remove_refuses_an_id_that_tries_to_escape_the_root(string id)
    {
        using var home = new TempHome();
        Assert.Throws<ArgumentException>(() => new WorkspaceStore(home.Path).Remove(id));
    }

    [Fact]
    public void Clean_removes_only_workspaces_older_than_the_cutoff()
    {
        using var home = new TempHome();
        var store = new WorkspaceStore(home.Path, () => DateTimeOffset.UtcNow);

        var oldRepo = "https://github.com/acme/old";
        var newRepo = "https://github.com/acme/new";
        foreach (var repo in new[] { oldRepo, newRepo })
        {
            Directory.CreateDirectory(store.PathFor(repo, "main"));
            store.Touch(WorkspaceStore.IdFor(repo, "main"), repo, "main", null, null);
        }

        // Re-stamp the old one through a store whose clock sits two years in the past.
        var past = new WorkspaceStore(home.Path, () => DateTimeOffset.UtcNow.AddYears(-2));
        past.Touch(WorkspaceStore.IdFor(oldRepo, "main"), oldRepo, "main", null, null);

        Assert.Equal(1, store.Clean(TimeSpan.FromDays(30)));
        Assert.False(Directory.Exists(store.PathFor(oldRepo, "main")));
        Assert.True(Directory.Exists(store.PathFor(newRepo, "main")));
    }

    [Fact]
    public void RemoveAll_empties_the_store()
    {
        using var home = new TempHome();
        var store = new WorkspaceStore(home.Path);
        foreach (var name in new[] { "a", "b" })
        {
            var repo = $"https://github.com/acme/{name}";
            Directory.CreateDirectory(store.PathFor(repo, "main"));
            store.Touch(WorkspaceStore.IdFor(repo, "main"), repo, "main", null, null);
        }

        Assert.Equal(2, store.RemoveAll());
        Assert.Empty(store.List());
    }

    [Fact]
    public void A_directory_without_metadata_is_not_listed()
    {
        using var home = new TempHome();
        var store = new WorkspaceStore(home.Path);
        Directory.CreateDirectory(Path.Combine(home.Path, "runs", "stray-directory"));
        Assert.Empty(store.List());
    }
}
