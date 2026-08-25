using QuickRun.App.Daemon;

namespace QuickRun.App.Tests;

/// <summary>
/// Reading back what <c>quickrun://</c> is registered to. A handler pointing at a binary that has
/// moved looks installed and does nothing, so the executable has to be recoverable from the
/// registered command line to be compared with the one running.
/// </summary>
public class SystemIntegrationTests
{
    [Fact]
    public void A_quoted_windows_command_yields_the_executable() =>
        Assert.Equal(@"C:\Program Files\QuickRun\quickrun.exe",
            SystemIntegration.ExecutableFrom(@"""C:\Program Files\QuickRun\quickrun.exe"" handle ""%1"""));

    [Fact]
    public void An_unquoted_desktop_exec_line_yields_the_executable() =>
        Assert.Equal("/home/me/.local/bin/quickrun",
            SystemIntegration.ExecutableFrom("/home/me/.local/bin/quickrun handle %u"));

    [Fact]
    public void A_command_that_is_only_the_executable_works() =>
        Assert.Equal("/usr/bin/quickrun", SystemIntegration.ExecutableFrom("  /usr/bin/quickrun  "));

    [Fact]
    public void Nothing_registered_yields_nothing()
    {
        Assert.Null(SystemIntegration.ExecutableFrom(""));
        Assert.Null(SystemIntegration.ExecutableFrom("   "));
        Assert.Null(SystemIntegration.ExecutableFrom("\"unterminated"));
    }

    /// <summary>The status call must answer on every platform rather than throw.</summary>
    [Fact]
    public void The_status_can_always_be_read()
    {
        var status = SystemIntegration.Status();
        Assert.False(string.IsNullOrWhiteSpace(status.Detail));
    }
}
