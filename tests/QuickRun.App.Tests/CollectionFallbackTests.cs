using QuickRun.App.Commands;
using QuickRun.Core;
using QuickRun.Core.Config;

namespace QuickRun.App.Tests;

/// <summary>
/// The order the config chain resolves in, now that the collection is part of it.
/// <para>
/// The rule: a repository's own quickrun.yml always wins. The collection speaks only for a
/// repository that ships nothing, and it speaks before the detector - a config somebody wrote for
/// that repository beats one guessed from its file names. The other direction would be the serious
/// mistake: a collected config overriding what a repository itself says about how to run.
/// </para>
/// <para>
/// Tested as what it is - an order - rather than through a checkout, so no repository is cloned and
/// nothing reaches the network. The lookup and its safety are covered in ConfigCollectionTests.
/// </para>
/// </summary>
public class CollectionFallbackTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("quickrun-chain-root").FullName;
    private readonly string _cache = Directory.CreateTempSubdirectory("quickrun-chain-cache").FullName;
    private readonly string _overrides = Directory.CreateTempSubdirectory("quickrun-chain-mine").FullName;

    private const string Repo = "acme/app";

    public CollectionFallbackTests() =>
        Environment.SetEnvironmentVariable(ConfigCollection.OptOut, null);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(ConfigCollection.OptOut, null);

        foreach (var dir in new[] { _root, _cache, _overrides })
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>A config in the checkout, as a repository that ships one would have.</summary>
    private void RepositoryShips(string yaml) =>
        File.WriteAllText(Path.Combine(_root, "quickrun.yml"), yaml);

    /// <summary>
    /// A collected config for this repository, already cached - which is how it is read without a
    /// request going anywhere.
    /// </summary>
    private void CollectionHolds(string yaml) =>
        File.WriteAllText(
            Path.Combine(_cache, ConfigCollection.RepoPath(Repo)!.Replace('/', '_') + ".yml"),
            yaml);

    private (RunConfig? Config, string? Error, IReadOnlyList<string> Notes, ConfigOrigin Origin) Load(
        bool fromCollection = false)
    {
        var args = new RunArgs("", null, null, null, Array.Empty<string>(), null,
            Fresh: false, Yes: true, NoOpen: true, ConfigPath: null,
            FromCollection: fromCollection);

        var notes = new List<string>();
        var loaded = RunPipeline.LoadConfig(_root, args, Repo, new ConfigOverrides(_overrides), _cache, notes);

        return (loaded.Config, loaded.Error, loaded.Notes.Concat(notes).ToList(), loaded.Origin);
    }

    [Fact]
    public void A_repository_with_its_own_config_never_consults_the_collection()
    {
        RepositoryShips("""
            name: The repository's own
            tasks:
              - run: echo own
            """);

        CollectionHolds("name: Collected\ntasks: [{run: echo collected}]\n");

        var (config, error, _, origin) = Load();

        Assert.Null(error);
        Assert.Equal(ConfigOrigin.Repository, origin);
        Assert.Equal("The repository's own", config!.Name);
    }

    [Fact]
    public void A_repository_with_nothing_uses_the_collection_before_guessing()
    {
        CollectionHolds("""
            name: Collected
            tasks:
              - run: echo collected
            """);

        var (config, error, notes, origin) = Load();

        Assert.Null(error);
        Assert.Equal(ConfigOrigin.Collection, origin);
        Assert.Equal("Collected", config!.Name);

        // And it says so: these commands are not the repository's.
        Assert.Contains(notes, n => n.Contains("collection", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void With_the_lookup_switched_off_it_behaves_exactly_as_before()
    {
        CollectionHolds("name: Collected\ntasks: [{run: echo collected}]\n");
        Environment.SetEnvironmentVariable(ConfigCollection.OptOut, "1");

        var (_, _, _, origin) = Load();

        Assert.NotEqual(ConfigOrigin.Collection, origin);
    }

    [Fact]
    public void A_broken_collected_config_is_skipped_rather_than_failing_the_run()
    {
        CollectionHolds("tasks:\n  - run: echo hi\n    unknownKey: 1\n");

        // Something for the detector to find, so this test can tell "carried on past it" apart from
        // "there was nothing to run either way".
        File.WriteAllText(Path.Combine(_root, "package.json"),
            """{"name": "demo", "scripts": {"dev": "node server.js"}}""");

        var (config, error, notes, origin) = Load();

        // Ours and wrong is still wrong, and the detector is right behind it. Said out loud, or it
        // would never get fixed.
        Assert.Equal(ConfigOrigin.Detected, origin);
        Assert.Contains(notes, n => n.Contains("broken", StringComparison.OrdinalIgnoreCase));

        // And it did not take the run down with it.
        Assert.Null(error);
        Assert.NotNull(config);
    }

    /// <summary>
    /// Asked for by name, the collected config runs - even against a repository that ships one.
    /// <para>
    /// This is the Run button on the collection page, which shows a config and then has to run that
    /// config. Anything else is a button that says one thing and does another.
    /// </para>
    /// </summary>
    [Fact]
    public void Asked_for_by_name_the_collection_wins_over_the_repositorys_own()
    {
        RepositoryShips("name: The repository's own\ntasks: [{run: echo own}]\n");
        CollectionHolds("name: Collected\ntasks: [{run: echo collected}]\n");

        var (config, error, notes, origin) = Load(fromCollection: true);

        Assert.Null(error);
        Assert.Equal(ConfigOrigin.Collection, origin);
        Assert.Equal("Collected", config!.Name);

        // And it says that it went past the repository's own, which is the surprising half.
        Assert.Contains(notes, n => n.Contains("quickrun.yml this repository ships", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_being_asked_the_repositorys_own_still_wins()
    {
        RepositoryShips("name: The repository's own\ntasks: [{run: echo own}]\n");
        CollectionHolds("name: Collected\ntasks: [{run: echo collected}]\n");

        // The automatic chain is untouched - this is the case that must not have changed.
        var (config, _, _, origin) = Load();

        Assert.Equal(ConfigOrigin.Repository, origin);
        Assert.Equal("The repository's own", config!.Name);
    }

    [Fact]
    public void Asked_for_by_name_with_nothing_kept_says_so()
    {
        RepositoryShips("name: The repository's own\ntasks: [{run: echo own}]\n");

        var (config, error, _, _) = Load(fromCollection: true);

        // Falling back to the repository's config here would silently run something other than what
        // the page showed. Better to say there is nothing to run.
        Assert.Null(config);
        Assert.Contains("keeps no config", error);
    }

    [Fact]
    public void Asked_for_by_name_the_collection_also_beats_your_own_saved_config()
    {
        new ConfigOverrides(_overrides).Write(Repo, "name: Mine\ntasks: [{run: echo mine}]\n");
        CollectionHolds("name: Collected\ntasks: [{run: echo collected}]\n");

        // Both are deliberate, and the more recent deliberate act is the button just pressed.
        var (config, _, _, origin) = Load(fromCollection: true);

        Assert.Equal(ConfigOrigin.Collection, origin);
        Assert.Equal("Collected", config!.Name);
    }

    [Fact]
    public void Your_own_saved_config_still_beats_the_collection()
    {
        new ConfigOverrides(_overrides).Write(Repo, "name: Mine\ntasks: [{run: echo mine}]\n");
        CollectionHolds("name: Collected\ntasks: [{run: echo collected}]\n");

        var (config, _, _, origin) = Load();

        Assert.Equal(ConfigOrigin.Local, origin);
        Assert.Equal("Mine", config!.Name);
    }
}
