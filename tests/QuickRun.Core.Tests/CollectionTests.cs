using QuickRun.Core.Config;

namespace QuickRun.Core.Tests;

/// <summary>
/// Every config QuickRun keeps for somebody else's repository.
/// <para>
/// These are served to other people's machines and run there, so a broken one is not a broken file -
/// it is a run that fails on a stranger's computer for a reason they cannot see. The whole collection
/// is parsed and validated here, and one bad config fails the build.
/// </para>
/// </summary>
public class CollectionTests
{
    /// <summary>The collection, found from the test assembly rather than assumed.</summary>
    private static string CollectionDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "configs")))
            dir = dir.Parent;

        return dir is null ? "" : Path.Combine(dir.FullName, "configs");
    }

    public static TheoryData<string> EveryConfig()
    {
        var data = new TheoryData<string>();
        var root = CollectionDir();

        if (root.Length == 0) return data;

        foreach (var file in Directory.EnumerateFiles(root, "*.yml", SearchOption.AllDirectories))
            data.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));

        return data;
    }

    [Fact]
    public void The_collection_is_there_and_not_empty()
    {
        var root = CollectionDir();

        Assert.True(root.Length > 0, "no configs directory found above the test assembly");
        Assert.NotEmpty(Directory.EnumerateFiles(root, "*.yml", SearchOption.AllDirectories));
    }

    [Theory]
    [MemberData(nameof(EveryConfig))]
    public void Every_config_parses_and_validates(string relative)
    {
        var path = Path.Combine(CollectionDir(), relative);
        var text = File.ReadAllText(path);

        var config = ConfigParser.Parse(text, OSKinds.Current);

        var errors = ConfigValidator.Validate(config).Where(i => i.IsError).ToList();
        Assert.True(errors.Count == 0,
            $"{relative}: {string.Join("; ", errors.Select(e => e.Message))}");

        // Every one of these is for a repository somewhere else, so it has to say which - that is
        // the whole reason the field exists.
        Assert.False(string.IsNullOrWhiteSpace(config.Repository), $"{relative} names no repository");

        // And it has to be the repository the file is filed under, or the lookup finds the wrong one.
        var expected = relative[..^".yml".Length];
        Assert.Equal(expected, ConfigCollection.RepoPath(config.Repository));

        Assert.NotEmpty(config.Tasks);
    }

    /// <summary>
    /// A config that starts containers has to stop them.
    /// <para>
    /// Killing the docker client leaves the container running - the same shape of leftover that had
    /// a run reporting "stopped" while it still answered on its port. So anything that runs docker
    /// says how it ends.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryConfig))]
    public void A_config_that_starts_containers_stops_them(string relative)
    {
        var path = Path.Combine(CollectionDir(), relative);
        var config = ConfigParser.Parse(File.ReadAllText(path), OSKinds.Current);

        var starts = config.Tasks
            .Where(t => t.Run.Contains("docker run", StringComparison.Ordinal))
            .ToList();

        if (starts.Count == 0) return;

        foreach (var task in starts)
        {
            // The name it runs under is what the stop commands have to name.
            var name = Name(task.Run);
            Assert.NotNull(name);

            Assert.Contains(config.Stop,
                step => step.Run.Contains($"docker rm -f {name}", StringComparison.Ordinal));
        }
    }

    private static string? Name(string command)
    {
        const string marker = "--name ";
        var at = command.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) return null;

        var rest = command[(at + marker.Length)..];
        var end = rest.IndexOf(' ');
        return end < 0 ? rest : rest[..end];
    }
}
