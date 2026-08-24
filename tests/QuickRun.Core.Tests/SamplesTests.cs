using QuickRun.Core.Config;

namespace QuickRun.Core.Tests;

/// <summary>
/// Locks the documented examples to the engine. The per-platform loop is the point: a sample using
/// a bare `run: ./run.sh` passes on Linux and fails the platform check on Windows, and this catches
/// that before the docs ship it.
/// </summary>
public class SamplesTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "samples"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("samples/ not found above the test binary");
    }

    private static string SamplesDir() => Path.Combine(RepoRoot(), "samples");

    public static TheoryData<string> SampleFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(SamplesDir(), "*.yml")) data.Add(Path.GetFileName(file));
        return data;
    }

    [Fact]
    public void There_is_at_least_one_sample()
        => Assert.NotEmpty(Directory.GetFiles(SamplesDir(), "*.yml"));

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void Every_sample_parses_and_validates_on_every_platform(string fileName)
    {
        var yaml = File.ReadAllText(Path.Combine(SamplesDir(), fileName));

        foreach (var os in new[] { OSKind.Windows, OSKind.Linux, OSKind.MacOs })
        {
            var config = ConfigParser.Parse(yaml, os);
            var errors = ConfigValidator.Validate(config).Where(i => i.IsError).ToList();
            Assert.Empty(errors);
        }
    }

    [Fact]
    public void Quickrun_own_config_parses_and_validates()
    {
        var yaml = File.ReadAllText(Path.Combine(RepoRoot(), "quickrun.yml"));
        var errors = ConfigValidator.Validate(ConfigParser.Parse(yaml, OSKinds.Current))
            .Where(i => i.IsError).ToList();
        Assert.Empty(errors);
    }
}
