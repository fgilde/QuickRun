using QuickRun.App.Commands;
using QuickRun.Core;
using QuickRun.Core.Config;
using QuickRun.Core.Tests;

namespace QuickRun.App.Tests;

public class DetectCommandTests
{
    [Fact]
    public void A_repository_with_a_detectable_entry_point_returns_zero()
    {
        using var repo = new FakeRepo().With("docker-compose.yml", "services: {}");
        var result = DetectCommand.Find(repo.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("compose", Assert.Single(result.Candidates).Kind);
    }

    [Fact]
    public void An_empty_repository_returns_one()
    {
        using var repo = new FakeRepo();
        var result = DetectCommand.Find(repo.Path);

        Assert.Equal(1, result.ExitCode);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void A_missing_directory_returns_two()
        => Assert.Equal(2, DetectCommand.Find(Path.Combine(Path.GetTempPath(), "quickrun-nope-8f2a")).ExitCode);

    [Fact]
    public void Save_writes_a_config_the_parser_accepts()
    {
        using var repo = new FakeRepo().With("package.json", "{\"scripts\":{\"dev\":\"vite\"}}");
        var candidate = DetectCommand.Find(repo.Path).Candidates[0];

        var (exitCode, path, error) = DetectCommand.Save(repo.Path, candidate);

        Assert.Equal(0, exitCode);
        Assert.Null(error);
        var parsed = ConfigParser.Parse(File.ReadAllText(path!), OSKinds.Current);
        Assert.Equal("npm run dev", Assert.Single(parsed.Tasks).Run);
    }

    [Fact]
    public void Save_refuses_to_overwrite_an_existing_config()
    {
        using var repo = new FakeRepo()
            .With("docker-compose.yml", "services: {}")
            .With("quickrun.yml", "run: ./handwritten.sh");
        var candidate = DetectCommand.Find(repo.Path).Candidates[0];

        var (exitCode, path, error) = DetectCommand.Save(repo.Path, candidate);

        Assert.Equal(1, exitCode);
        Assert.Null(path);
        Assert.Contains("already exists", error!);
        Assert.Equal("run: ./handwritten.sh", File.ReadAllText(Path.Combine(repo.Path, "quickrun.yml")));
    }
}
