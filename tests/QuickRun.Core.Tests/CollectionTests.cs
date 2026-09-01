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

    /// <summary>
    /// A database container is given the credentials the application connects with.
    /// <para>
    /// This is the defect that broke twenty-nine of these configs: the catalogue supplies the user,
    /// the password and the database name, the generator dropped them, and a postgres started with
    /// none of the three refuses to initialise. The application then comes up against a database
    /// that will not have it, which looks like the application being broken.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryConfig))]
    public void A_database_gets_its_credentials(string relative)
    {
        var path = Path.Combine(CollectionDir(), relative);
        var config = ConfigParser.Parse(File.ReadAllText(path), OSKinds.Current);

        // What each image will not start without. Redis and mongo take none, so they are not here.
        var required = new (string Image, string[] Any)[]
        {
            ("postgres", ["POSTGRES_PASSWORD", "POSTGRES_HOST_AUTH_METHOD"]),
            ("mysql", ["MYSQL_ROOT_PASSWORD", "MYSQL_ALLOW_EMPTY_PASSWORD", "MYSQL_RANDOM_ROOT_PASSWORD"]),
            ("mariadb", ["MARIADB_ROOT_PASSWORD", "MARIADB_ALLOW_EMPTY_ROOT_PASSWORD",
                "MARIADB_RANDOM_ROOT_PASSWORD", "MYSQL_ROOT_PASSWORD", "MYSQL_RANDOM_ROOT_PASSWORD"]),
        };

        foreach (var task in config.Tasks)
        foreach (var (image, any) in required)
        {
            // The image, not the environment: DATABASE_URL says "postgresql" in half of these, and
            // the application container is not the one that needs the credentials.
            if (!Runs(task.Run, image)) continue;

            Assert.True(any.Any(key => task.Run.Contains(key, StringComparison.Ordinal)),
                $"{relative}: task '{task.Name}' starts {image} without any of {string.Join(", ", any)}");
        }
    }

    /// <summary>
    /// Whether the command starts this image, told apart from merely mentioning its name.
    /// <para>
    /// The image is the last word of a docker run, and everything before it is flags and values -
    /// including a connection string that says postgresql and belongs to something else.
    /// </para>
    /// </summary>
    private static bool Runs(string command, string image)
    {
        if (!command.Contains("docker run", StringComparison.Ordinal)) return false;

        var last = command.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
        return last.StartsWith(image + ":", StringComparison.OrdinalIgnoreCase)
            || last.Equals(image, StringComparison.OrdinalIgnoreCase)
            || last.Contains("/" + image + ":", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A task something else waits for says when it is ready.
    /// <para>
    /// Without it a task counts as ready the moment docker was asked to start it, and the
    /// application opens its connection to a database that is not listening yet. dependsOn then
    /// buys the order and none of the waiting it was written for.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryConfig))]
    public void A_task_others_wait_for_says_when_it_is_ready(string relative)
    {
        var path = Path.Combine(CollectionDir(), relative);
        var config = ConfigParser.Parse(File.ReadAllText(path), OSKinds.Current);

        var awaited = config.Tasks.SelectMany(t => t.DependsOn).ToHashSet(StringComparer.Ordinal);

        foreach (var task in config.Tasks.Where(t => awaited.Contains(t.Name)))
            Assert.True(task.ReadyWhen is not null,
                $"{relative}: task '{task.Name}' is waited for but never says when it is ready");
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
