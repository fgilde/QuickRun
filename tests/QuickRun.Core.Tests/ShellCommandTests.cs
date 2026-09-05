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

    /// <summary>
    /// cmd.exe is handed its line whole, and every other shell an argument list.
    /// <para>
    /// Tested here, on the line itself, because no test can see it from the outside: whether the
    /// damage shows depends on how the program being started parses its arguments. node and git use
    /// the C runtime's rules and put <c>\"</c> back together again, so they never noticed; docker
    /// does not, and it received the quotes as characters. A test that started node would have
    /// passed with the bug still in place - it was written, it did, and it was deleted.
    /// </para>
    /// <para>
    /// .NET escapes an argument list the way the C runtime parses it, so a quote travels as
    /// <c>\"</c>. Programs that parse arguments that way put the string back together - bash and
    /// git among them - but cmd.exe has rules of its own and passes <c>\"</c> through. That turned
    /// <c>-e PASSWORD="secret"</c> into a password with quotes in it, which is a database nobody can
    /// log into and an error message about the wrong thing.
    /// </para>
    /// </summary>
    [Fact]
    public void Cmd_is_handed_its_line_whole()
    {
        var line = ShellCommand.RawCommandLine("cmd.exe", new[] { "/c", "docker run -e A=\"b\"" });

        // /s is what makes it exact: cmd strips the first and last character when both are quotes
        // and takes the rest verbatim. Without it, what happens to them depends on the rest of the
        // line.
        Assert.Equal("/s /c \"docker run -e A=\"b\"\"", line);
    }

    [Theory]
    [InlineData("/bin/sh", "-c")]
    [InlineData(@"C:\Program Files\Git\bin\bash.exe", "-c")]
    public void Every_other_shell_keeps_its_argument_list(string file, string flag)
    {
        // Nothing to correct here: these parse arguments the way .NET writes them, and taking the
        // quoting into our own hands would be the risk rather than the fix.
        Assert.Null(ShellCommand.RawCommandLine(file, new[] { flag, "echo hi" }));
    }

    [Fact]
    public void Anything_that_is_not_a_shell_invocation_keeps_its_argument_list()
    {
        // A tool run directly - git, node - is given its arguments one by one, and this must not
        // reach in and rewrite that.
        Assert.Null(ShellCommand.RawCommandLine("git", new[] { "clone", "https://example.com/x" }));
        Assert.Null(ShellCommand.RawCommandLine("cmd.exe", new[] { "/k", "echo hi" }));
    }
}
