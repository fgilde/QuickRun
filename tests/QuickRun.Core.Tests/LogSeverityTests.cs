using QuickRun.Core.Run;

namespace QuickRun.Core.Tests;

/// <summary>
/// Reading somebody else's output and saying how bad a line is.
/// <para>
/// A guess by nature - this is arbitrary output from an arbitrary program. What it must not do is
/// cry wolf: a log where the successful end of a build is painted red teaches the reader that red
/// means nothing, and the next real error is invisible. So the lines below are real output from the
/// tools QuickRun actually runs, and the half of them that matters is the half that must stay quiet.
/// </para>
/// </summary>
public class LogSeverityTests
{
    [Theory]
    // npm, and the exclamation mark it puts where a word would be.
    [InlineData("npm ERR! code ELIFECYCLE")]
    [InlineData("npm ERR! errno 1")]
    // MSBuild and the compilers.
    [InlineData("src/Program.cs(12,5): error CS1002: ; expected")]
    [InlineData("error MSB4018: The \"Csc\" task failed unexpectedly.")]
    // Python.
    [InlineData("Traceback (most recent call last):")]
    [InlineData("ModuleNotFoundError: No module named 'flask'")]
    // Docker and the go tools.
    [InlineData("time=\"2026-09-11T10:00:00Z\" level=error msg=\"pull access denied\"")]
    [InlineData("docker: Error response from daemon: conflict.")]
    // The operating system, through whatever ran into it.
    [InlineData("bash: line 1: pnpm: command not found")]
    [InlineData("Error: listen EADDRINUSE: address already in use :::3000")]
    [InlineData("cp: cannot stat 'dist/app': No such file or directory")]
    [InlineData("Error: connect ECONNREFUSED 127.0.0.1:5432")]
    [InlineData("2 tests failed")]
    public void A_line_that_says_something_broke_is_an_error(string line) =>
        Assert.Equal(LogSeverity.Error, LogSeverities.Of(line));

    [Theory]
    [InlineData("fatal: repository 'https://github.com/nope/nope' not found")]
    [InlineData("panic: runtime error: invalid memory address")]
    [InlineData("Unhandled exception. System.InvalidOperationException: no")]
    [InlineData("/entrypoint.sh: line 3:  7 Segmentation fault      ./server")]
    public void A_line_that_ends_everything_is_fatal(string line) =>
        Assert.Equal(LogSeverity.Fatal, LogSeverities.Of(line));

    [Theory]
    [InlineData("npm WARN deprecated request@2.88.2: request has been deprecated")]
    [InlineData("warning CS0168: The variable 'e' is declared but never used")]
    [InlineData("[warning] Some files were ignored")]
    [InlineData("level=warning msg=\"no swap limit support\"")]
    // The vague ones: printed as often about a fallback as about a failure, so they stay warnings.
    [InlineData("Cache not found for key: node-modules-abc123")]
    [InlineData("Unable to find image locally, pulling")]
    [InlineData("invalid tsconfig option ignored: incremental")]
    [InlineData("Skipping optional dependency fsevents")]
    public void A_line_worth_knowing_about_is_a_warning(string line) =>
        Assert.Equal(LogSeverity.Warn, LogSeverities.Of(line));

    /// <summary>
    /// The half that matters. Every one of these carries a word that names a problem, and every one
    /// of them is a line from a run that went perfectly.
    /// </summary>
    [Theory]
    [InlineData("Build succeeded.")]
    [InlineData("    0 Warning(s)")]
    [InlineData("    0 Error(s)")]
    [InlineData("found 0 vulnerabilities")]
    [InlineData("Passed!  - Failed:     0, Passed:  1327, Skipped:     0")]
    [InlineData("eslint: no errors found")]
    [InlineData("Compiled successfully in 3.4s")]
    [InlineData("VITE v5.4.2  ready in 412 ms")]
    [InlineData("Listening on http://localhost:3000")]
    // The word inside a name, which a build prints for every file it touches.
    [InlineData("  compiling src/pages/ErrorPage.tsx")]
    [InlineData("wrote dist/errorless.min.js")]
    public void A_line_that_only_mentions_trouble_is_not_trouble(string line) =>
        Assert.Equal(LogSeverity.Info, LogSeverities.Of(line));

    /// <summary>
    /// Standard error is not an error stream, whatever it is called.
    /// <para>
    /// docker, git, npm and every progress bar write their ordinary output there. Counting it as
    /// errors is exactly how "17 error lines" ends up above a run that worked - and that number is
    /// the thing a reader uses to decide whether to look. It lifts an unrecognised line to a
    /// warning, which is what the stream honestly says: not the normal output.
    /// </para>
    /// </summary>
    [Fact]
    public void Standard_error_is_a_warning_at_most()
    {
        Assert.Equal(LogSeverity.Warn,
            LogSeverities.Of("Pulling from library/postgres", fromErrorStream: true));

        // And a line that does say something broke is still an error, wherever it came from.
        Assert.Equal(LogSeverity.Error,
            LogSeverities.Of("npm ERR! code ELIFECYCLE", fromErrorStream: true));

        // A denial on that stream stays a denial.
        Assert.Equal(LogSeverity.Warn,
            LogSeverities.Of("found 0 vulnerabilities", fromErrorStream: true));
    }

    [Fact]
    public void Nothing_is_nothing()
    {
        Assert.Equal(LogSeverity.Info, LogSeverities.Of(null));
        Assert.Equal(LogSeverity.Info, LogSeverities.Of(""));
        Assert.Equal(LogSeverity.Info, LogSeverities.Of("   "));
    }

    [Fact]
    public void The_name_is_what_a_filter_and_a_colour_are_keyed_on()
    {
        Assert.Equal("fatal", LogSeverity.Fatal.Name());
        Assert.Equal("error", LogSeverity.Error.Name());
        Assert.Equal("warn", LogSeverity.Warn.Name());
        Assert.Equal("info", LogSeverity.Info.Name());
    }
}
