using QuickRun.Core.Process;

namespace QuickRun.Core.Tests;

public class CommandRunnerTests
{
    [Fact]
    public void Capture_returns_stdout_and_exit_code()
    {
        var (file, args) = ShellCommand.Resolve("echo hello-quickrun");
        var result = CommandRunner.Capture(file, args);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello-quickrun", result.Output);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public void Capture_reports_a_nonzero_exit_code()
    {
        var (file, args) = ShellCommand.Resolve("exit 3");
        Assert.Equal(3, CommandRunner.Capture(file, args).ExitCode);
    }

    [Fact]
    public void Capture_reports_a_missing_executable_without_throwing()
    {
        var result = CommandRunner.Capture("definitely-not-a-real-binary-9876", Array.Empty<string>());
        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task StreamAsync_delivers_lines_as_they_arrive()
    {
        var lines = new List<string>();
        var code = await CommandRunner.StreamAsync(
            new ProcessSpec("echo one && echo two", null, null),
            (line, _) => { lock (lines) lines.Add(line); },
            CancellationToken.None);

        Assert.Equal(0, code);
        var text = string.Join("\n", lines);
        Assert.Contains("one", text);
        Assert.Contains("two", text);
    }

    [Fact]
    public async Task StreamAsync_passes_environment_variables_through()
    {
        var env = new Dictionary<string, string> { ["QUICKRUN_TEST_VALUE"] = "42" };
        var command = OSKinds.Current == OSKind.Windows ? "echo %QUICKRUN_TEST_VALUE%" : "echo $QUICKRUN_TEST_VALUE";
        var lines = new List<string>();
        await CommandRunner.StreamAsync(new ProcessSpec(command, null, env),
            (line, _) => { lock (lines) lines.Add(line); }, CancellationToken.None);
        Assert.Contains("42", string.Join("\n", lines));
    }

    [Fact]
    public async Task StreamAsync_runs_in_the_requested_working_directory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "quickrun-cwd-" + Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "marker.txt"), "x");
        try
        {
            var command = OSKinds.Current == OSKind.Windows ? "dir /b" : "ls";
            var lines = new List<string>();
            await CommandRunner.StreamAsync(new ProcessSpec(command, dir, null),
                (line, _) => { lock (lines) lines.Add(line); }, CancellationToken.None);
            Assert.Contains("marker.txt", string.Join("\n", lines));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task StreamAsync_kills_a_long_running_process_on_cancellation()
    {
        using var cts = new CancellationTokenSource();
        var sleep = OSKinds.Current == OSKind.Windows ? "ping -n 60 127.0.0.1 >nul" : "sleep 60";
        var run = CommandRunner.StreamAsync(new ProcessSpec(sleep, null, null), (_, _) => { }, cts.Token);

        await Task.Delay(300);
        await cts.CancelAsync();

        var code = await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotEqual(0, code);
    }
}
