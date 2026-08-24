using QuickRun.Core.Process;

namespace QuickRun.Core.Tests;

public class ShellCommandTests
{
    private static bool NoBash(string path) => false;
    private static bool GitBash(string path) => path.EndsWith("bash.exe", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Linux_uses_sh_dash_c()
    {
        var (file, args) = ShellCommand.Resolve("npm run dev", OSKind.Linux, NoBash);
        Assert.Equal("/bin/sh", file);
        Assert.Equal(new[] { "-c", "npm run dev" }, args);
    }

    [Fact]
    public void Windows_uses_cmd_slash_c()
    {
        var (file, args) = ShellCommand.Resolve("npm run dev", OSKind.Windows, NoBash);
        Assert.Equal("cmd.exe", file);
        Assert.Equal(new[] { "/c", "npm run dev" }, args);
    }

    [Fact]
    public void Windows_routes_sh_scripts_through_git_bash_when_present()
    {
        var (file, args) = ShellCommand.Resolve("./run.sh --fast", OSKind.Windows, GitBash);
        Assert.EndsWith("bash.exe", file);
        Assert.Equal(new[] { "-c", "./run.sh --fast" }, args);
    }

    [Fact]
    public void Windows_falls_back_to_cmd_when_git_bash_is_missing()
    {
        var (file, _) = ShellCommand.Resolve("./run.sh", OSKind.Windows, NoBash);
        Assert.Equal("cmd.exe", file);
    }

    [Theory]
    [InlineData(OSKind.Windows, "windows")]
    [InlineData(OSKind.Linux, "linux")]
    [InlineData(OSKind.MacOs, "macos")]
    public void Key_matches_the_platform_names_used_in_configs(OSKind os, string expected)
        => Assert.Equal(expected, os.Key());
}
