using QuickRun.Core.Config;

namespace QuickRun.Core.Tests;

/// <summary>
/// Running a single config file, and what it is allowed to mean.
/// <para>
/// The dangerous half is the folder rule: "no repository named, so run the directory this file is
/// in" is reasonable for a file somebody picked here, and is not something a link may decide - it
/// would turn a downloaded yml into a way of running whatever is in the downloads folder. So the
/// permission is a parameter, and these tests hold both halves of it in place.
/// </para>
/// </summary>
public class ConfigFileRunTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("quickrun-configfile").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Write(string name, string yaml)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, yaml);
        return path;
    }

    [Fact]
    public void A_config_that_names_a_repository_is_about_that_repository()
    {
        var path = Write("demo.yml", """
            name: Demo
            repository: acme/app
            tasks:
              - run: echo hi
            """);

        var target = ConfigFileRun.Read(path, OSKinds.Current, allowFolder: true);

        Assert.Null(target.Error);
        Assert.Equal("acme/app", target.Repo);
        Assert.Null(target.LocalFolder);
        Assert.Contains("echo hi", target.Text);
    }

    [Fact]
    public void A_repository_may_be_a_url_and_carry_a_ref()
    {
        var path = Write("demo.yml", """
            repository: https://github.com/acme/app
            ref: preview
            tasks:
              - run: echo hi
            """);

        var target = ConfigFileRun.Read(path, OSKinds.Current, allowFolder: true);

        Assert.Equal("https://github.com/acme/app", target.Repo);
        Assert.Equal("preview", target.Ref);
    }

    [Fact]
    public void Without_a_repository_the_file_is_about_the_code_beside_it()
    {
        var path = Write("quickrun.yml", """
            tasks:
              - run: echo hi
            """);

        var target = ConfigFileRun.Read(path, OSKinds.Current, allowFolder: true);

        Assert.Null(target.Error);
        Assert.Null(target.Repo);
        Assert.Equal(_dir, target.LocalFolder);
    }

    [Fact]
    public void Without_a_repository_and_without_permission_it_asks_for_one()
    {
        var path = Write("quickrun.yml", """
            tasks:
              - run: echo hi
            """);

        var target = ConfigFileRun.Read(path, OSKinds.Current, allowFolder: false);

        Assert.True(target.NeedsRepository);
        Assert.Null(target.Repo);
        Assert.Null(target.LocalFolder);

        // The config still travels, so whoever answers the question does not have to open it again.
        Assert.Contains("echo hi", target.Text);
    }

    [Fact]
    public void A_named_repository_needs_no_permission_for_a_folder()
    {
        // The interesting combination: the file arrived from somewhere untrusted, but it says what
        // it is for, so nothing about this machine's directories is being decided.
        var path = Write("demo.yml", """
            repository: acme/app
            tasks:
              - run: echo hi
            """);

        var target = ConfigFileRun.Read(path, OSKinds.Current, allowFolder: false);

        Assert.Null(target.Error);
        Assert.Equal("acme/app", target.Repo);
        Assert.False(target.NeedsRepository);
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("config.yml.exe")]
    public void Only_a_yml_file_is_a_config(string name)
    {
        var path = Write(name, "tasks: []");

        var target = ConfigFileRun.Read(path, OSKinds.Current, allowFolder: true);

        Assert.NotNull(target.Error);
        Assert.Null(target.Text);
    }

    [Fact]
    public void A_file_that_is_not_there_says_so()
    {
        var target = ConfigFileRun.Read(Path.Combine(_dir, "missing.yml"), OSKinds.Current, true);

        Assert.Contains("no file", target.Error);
    }

    [Fact]
    public void Nothing_named_is_not_a_run()
    {
        Assert.NotNull(ConfigFileRun.Read(null, OSKinds.Current, true).Error);
        Assert.NotNull(ConfigFileRun.Read("   ", OSKinds.Current, true).Error);
    }

    [Fact]
    public void A_broken_config_is_reported_as_a_config_problem()
    {
        var path = Write("bad.yml", """
            tasks:
              - run: echo hi
                unknownKey: 1
            """);

        var target = ConfigFileRun.Read(path, OSKinds.Current, allowFolder: true);

        Assert.NotNull(target.Error);
        Assert.Contains("bad.yml", target.Error);
        Assert.Contains("unknownKey", target.Error);
    }

    [Fact]
    public void Something_far_too_large_is_not_a_config()
    {
        var path = Path.Combine(_dir, "huge.yml");
        File.WriteAllText(path, new string('#', 600 * 1024));

        var target = ConfigFileRun.Read(path, OSKinds.Current, allowFolder: true);

        Assert.Contains("too large", target.Error);
    }
}
