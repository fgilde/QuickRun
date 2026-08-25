using QuickRun.Core.Config;

namespace QuickRun.Core.Tests;

/// <summary>
/// Your own config for someone else's repository. It lives in QuickRun's directory on purpose: in
/// the checkout it would be deleted by --fresh and committable by accident.
/// </summary>
public class ConfigOverridesTests
{
    private sealed class Home : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "quickrun-cfg-" + Guid.NewGuid().ToString("n")[..8]);

        public Home() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void What_was_written_comes_back()
    {
        using var home = new Home();
        var overrides = new ConfigOverrides(home.Path);

        Assert.Null(overrides.Read("https://github.com/acme/app"));
        Assert.False(overrides.Has("https://github.com/acme/app"));

        var path = overrides.Write("https://github.com/acme/app", "run: echo hi\n");

        Assert.True(overrides.Has("https://github.com/acme/app"));
        Assert.Equal("run: echo hi\n", overrides.Read("https://github.com/acme/app"));
        Assert.EndsWith(ConfigOverrides.FileName, path);
        Assert.StartsWith(home.Path, path);
    }

    /// <summary>Whether the URL was typed with a .git suffix is not a different repository.</summary>
    [Fact]
    public void The_same_repository_spelled_differently_shares_one_override()
    {
        using var home = new Home();
        var overrides = new ConfigOverrides(home.Path);

        overrides.Write("https://github.com/acme/app", "run: echo one\n");

        Assert.Equal("run: echo one\n", overrides.Read("https://github.com/acme/app.git"));
        Assert.Equal("run: echo one\n", overrides.Read("https://github.com/acme/app/"));
    }

    /// <summary>Same path, different host: two projects, not one.</summary>
    [Fact]
    public void Two_hosts_with_the_same_path_do_not_collide()
    {
        using var home = new Home();
        var overrides = new ConfigOverrides(home.Path);

        overrides.Write("https://github.com/acme/app", "run: echo github\n");
        overrides.Write("https://gitlab.example/acme/app", "run: echo gitlab\n");

        Assert.Equal("run: echo github\n", overrides.Read("https://github.com/acme/app"));
        Assert.Equal("run: echo gitlab\n", overrides.Read("https://gitlab.example/acme/app"));
    }

    [Fact]
    public void Deleting_removes_the_file_and_leaves_no_empty_folder()
    {
        using var home = new Home();
        var overrides = new ConfigOverrides(home.Path);

        overrides.Write("https://github.com/acme/app", "run: echo hi\n");
        Assert.True(overrides.Delete("https://github.com/acme/app"));

        Assert.False(overrides.Has("https://github.com/acme/app"));
        Assert.False(Directory.Exists(Path.GetDirectoryName(overrides.PathFor("https://github.com/acme/app"))));
        Assert.False(overrides.Delete("https://github.com/acme/app"));
    }

    /// <summary>The folder name is a hash, so the listing has to remember what it was saved for.</summary>
    [Fact]
    public void The_listing_names_the_repository_it_belongs_to()
    {
        using var home = new Home();
        var overrides = new ConfigOverrides(home.Path);

        overrides.Remember("https://github.com/acme/app");
        overrides.Write("https://github.com/acme/app", "run: echo hi\n");

        var listed = Assert.Single(overrides.List());
        Assert.Equal("https://github.com/acme/app", listed.Repo);
        Assert.EndsWith(ConfigOverrides.FileName, listed.Path);
    }

    [Fact]
    public void Nothing_saved_lists_nothing()
    {
        using var home = new Home();
        Assert.Empty(new ConfigOverrides(home.Path).List());
    }

    [Fact]
    public void A_repository_name_is_recognisable_in_the_folder_name() =>
        Assert.StartsWith("acme_app-", ConfigOverrides.IdFor("https://github.com/acme/app"));
}
