using QuickRun.App.Commands;
using QuickRun.Core.Config;

namespace QuickRun.App.Tests;

/// <summary>
/// Which file a plan came out of, and its contents.
/// <para>
/// "The repository's quickrun.yml" says which of six sources was used. It does not say which file -
/// and a config saved on this machine, one out of QuickRun's collection and one the detector wrote
/// all produce a plan that reads exactly the same. The window names the file and can show it, and
/// this is the part that has to be right for that to mean anything: point at the wrong file and the
/// window is confidently misleading, which is worse than saying nothing.
/// </para>
/// </summary>
public class ConfigSourceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("quickrun-source-root").FullName;
    private readonly string _cache = Directory.CreateTempSubdirectory("quickrun-source-cache").FullName;
    private readonly string _overrides = Directory.CreateTempSubdirectory("quickrun-source-mine").FullName;

    private const string Repo = "acme/app";

    private const string Yaml = """
        name: Demo
        tasks:
          - name: web
            run: echo hi
        """;

    public ConfigSourceTests() =>
        Environment.SetEnvironmentVariable(ConfigCollection.OptOut, null);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(ConfigCollection.OptOut, null);

        foreach (var dir in new[] { _root, _cache, _overrides })
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }

    private Loaded Load(string? configPath = null, bool fromCollection = false)
    {
        var args = new RunArgs("", null, null, null, Array.Empty<string>(), null,
            Fresh: false, Yes: true, NoOpen: true, ConfigPath: configPath,
            FromCollection: fromCollection);

        return RunPipeline.LoadConfig(_root, args, Repo, new ConfigOverrides(_overrides), _cache,
            new List<string>());
    }

    [Fact]
    public void The_repositorys_own_config_is_named_by_its_full_path()
    {
        var file = Path.Combine(_root, "quickrun.yml");
        File.WriteAllText(file, Yaml);

        var loaded = Load();

        Assert.Equal(ConfigOrigin.Repository, loaded.Origin);
        Assert.Equal(Path.GetFullPath(file), loaded.Source);
        Assert.Equal(Yaml, loaded.Text);
    }

    [Fact]
    public void A_config_saved_on_this_machine_points_at_the_file_it_was_saved_in()
    {
        // The one that has to be distinguishable: it wins over the repository's own, so a window
        // naming the repository's file while running this one would be a lie the run cannot correct.
        File.WriteAllText(Path.Combine(_root, "quickrun.yml"), Yaml);

        var mine = new ConfigOverrides(_overrides);
        mine.Write(Repo, "name: Mine\ntasks:\n  - run: echo mine\n");

        var loaded = Load();

        Assert.Equal(ConfigOrigin.Local, loaded.Origin);
        Assert.Equal(mine.PathFor(Repo), loaded.Source);
        Assert.Contains("echo mine", loaded.Text);
    }

    [Fact]
    public void A_collected_config_points_at_the_copy_on_this_machine()
    {
        File.WriteAllText(ConfigCollection.FileFor(Repo, _cache)!, Yaml);

        var loaded = Load();

        Assert.Equal(ConfigOrigin.Collection, loaded.Origin);
        Assert.Equal(ConfigCollection.FileFor(Repo, _cache), loaded.Source);
        Assert.Equal(Yaml, loaded.Text);
    }

    [Fact]
    public void A_config_named_on_the_command_line_is_named_where_it_is()
    {
        Directory.CreateDirectory(Path.Combine(_root, "ci"));
        var file = Path.Combine(_root, "ci", "demo.quickrun.yml");
        File.WriteAllText(file, Yaml);

        var loaded = Load(configPath: Path.Combine("ci", "demo.quickrun.yml"));

        Assert.Equal(ConfigOrigin.Explicit, loaded.Origin);
        Assert.Equal(Path.GetFullPath(file), loaded.Source);
        Assert.Equal(Yaml, loaded.Text);
    }

    /// <summary>
    /// Nothing to point at, and the text still worth having.
    /// <para>
    /// There is no file for a config QuickRun wrote by reading the repository, so a path would have
    /// to be invented - and inventing one is exactly what must not happen on the line somebody
    /// reads before approving commands. The config itself is real, though, and it is the one a
    /// reader is most likely to want to look at and correct.
    /// </para>
    /// </summary>
    [Fact]
    public void A_detected_config_has_no_file_but_does_have_a_text()
    {
        File.WriteAllText(Path.Combine(_root, "docker-compose.yml"),
            "services:\n  web:\n    image: nginx\n");

        var loaded = Load();

        Assert.Equal(ConfigOrigin.Detected, loaded.Origin);
        Assert.Null(loaded.Source);
        Assert.NotNull(loaded.Text);
        Assert.Contains("tasks:", loaded.Text);
    }

    [Fact]
    public void A_config_that_could_not_be_read_points_nowhere()
    {
        var loaded = Load();

        Assert.NotNull(loaded.Error);
        Assert.Null(loaded.Source);
        Assert.Null(loaded.Text);
    }
}
