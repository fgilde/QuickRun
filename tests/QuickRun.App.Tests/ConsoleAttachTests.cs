namespace QuickRun.App.Tests;

public class ConsoleAttachTests
{
    [Fact]
    public void A_borrowed_console_is_never_asked_and_never_says_yes()
    {
        // Issue #1: cmd does not wait for a GUI-subsystem program, so the "y" typed in answer ran as
        // a command in cmd. The question must not be put at all, and the answer is no.
        var asked = false;

        Assert.False(ConsoleAttach.Confirm("Install QuickRun 1.0?", borrowed: true, () => asked = true));
        Assert.False(asked);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_console_of_its_own_gets_the_question(bool answer)
        => Assert.Equal(answer, ConsoleAttach.Confirm("Run these commands?", borrowed: false, () => answer));
}

public class OpensWindowTests
{
    [Theory]
    [InlineData(new string[0], true)]
    [InlineData(new[] { "ui" }, true)]
    [InlineData(new[] { "--port", "9999" }, true)]
    [InlineData(new[] { "--help" }, false)]
    [InlineData(new[] { "--version" }, false)]
    [InlineData(new[] { "run", "acme/app" }, false)]
    [InlineData(new[] { "update" }, false)]
    [InlineData(new[] { "ui", "--help" }, false)]
    public void Only_the_window_leaves_the_terminal_alone(string[] args, bool window)
        => Assert.Equal(window, Program.OpensWindow(args));
}
