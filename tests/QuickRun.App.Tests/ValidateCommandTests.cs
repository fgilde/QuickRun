using QuickRun.App.Commands;

namespace QuickRun.App.Tests;

public class ValidateCommandTests
{
    private static string WriteConfig(string yaml)
    {
        var dir = Path.Combine(Path.GetTempPath(), "quickrun-cli-" + Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "quickrun.yml"), yaml);
        return dir;
    }

    private static string EmptyDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "quickrun-empty-" + Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void A_valid_config_in_a_directory_returns_zero()
        => Assert.Equal(0, ValidateCommand.Check(WriteConfig("run: ./run.sh"), quiet: true).ExitCode);

    [Fact]
    public void A_config_with_an_error_returns_one()
    {
        var result = ValidateCommand.Check(
            WriteConfig("tasks:\n  - name: a\n    run: x\n    dependsOn: [nope]"), quiet: true);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(result.Issues, i => i.IsError);
    }

    [Fact]
    public void Malformed_yaml_returns_one_with_a_readable_message()
    {
        var result = ValidateCommand.Check(WriteConfig("run: [unclosed"), quiet: true);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("YAML", Assert.Single(result.Issues).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_directory_without_a_config_returns_two()
        => Assert.Equal(2, ValidateCommand.Check(EmptyDir(), quiet: true).ExitCode);

    [Fact]
    public void An_explicit_file_path_is_accepted()
    {
        var dir = WriteConfig("run: ./run.sh");
        Assert.Equal(0, ValidateCommand.Check(Path.Combine(dir, "quickrun.yml"), quiet: true).ExitCode);
    }

    [Fact]
    public void Warnings_alone_still_return_zero()
    {
        var result = ValidateCommand.Check(
            WriteConfig("inputs:\n  - id: k\n    type: bool\n    pattern: \"^x\"\nrun: a"), quiet: true);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.Issues, i => !i.IsError);
    }

    [Fact]
    public void The_parsed_config_travels_with_a_successful_result()
        => Assert.Equal("Demo", ValidateCommand.Check(WriteConfig("name: Demo\nrun: a"), quiet: true).Config!.Name);
}
