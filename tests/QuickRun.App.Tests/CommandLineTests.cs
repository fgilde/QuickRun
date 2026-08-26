namespace QuickRun.App.Tests;

/// <summary>
/// Every command, asked for its help.
/// <para>
/// A command whose settings the binder cannot construct, an option declared twice, an example that
/// names a switch which no longer exists - none of that shows up until someone runs that one
/// command, which for half of these is a release later. This walks all of them on every build.
/// </para>
/// </summary>
public class CommandLineTests
{
    /// <summary>Every command a user can type, hidden ones included.</summary>
    public static TheoryData<string> Commands() => new()
    {
        "run",
        "validate",
        "detect",
        "ui",
        "daemon",
        "install",
        "uninstall",
        "handle",
        "doctor",
        "update",
        "ls",
        "clean",
    };

    [Theory]
    [MemberData(nameof(Commands))]
    public void Every_command_can_describe_itself(string command)
    {
        Assert.Equal(0, Program.Build().Run([command, "--help"]));
    }

    [Fact]
    public void The_help_and_the_version_work_without_a_command()
    {
        Assert.Equal(0, Program.Build().Run(["--help"]));
        Assert.Equal(0, Program.Build().Run(["--version"]));
    }

    /// <summary>
    /// A word that is not a command must not quietly become one - least of all a repository to check
    /// out, which is the one thing here that touches the network and the disk. It is reported as a
    /// usage error, which the entry point turns into exit code 2.
    /// </summary>
    [Fact]
    public void An_unknown_command_is_a_usage_error()
    {
        Assert.ThrowsAny<Exception>(() => Program.Build().Run(["nonsense"]));
    }
}
