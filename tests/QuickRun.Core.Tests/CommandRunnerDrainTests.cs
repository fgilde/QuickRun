using System.Diagnostics;
using QuickRun.Core.Process;

namespace QuickRun.Core.Tests;

/// <summary>
/// A command is finished when its process is gone, not when its pipes close.
/// <para>
/// MSBuild leaves reusable worker nodes behind that inherit the build's output handles and live for
/// minutes. Waiting for end of file on those handles made a finished <c>dotnet restore</c> look like
/// a run frozen at the setup step - with the whole restore already printed above it.
/// </para>
/// </summary>
public class CommandRunnerDrainTests
{
    /// <summary>A command that leaves a background child holding the output handle.</summary>
    private static string CommandWithLingeringChild =>
        OSKinds.Current == OSKind.Windows
            ? "powershell -NoProfile -Command \"Start-Process -NoNewWindow powershell "
              + "-ArgumentList '-NoProfile','-Command','Start-Sleep -Seconds 30'; 'parent done'\""
            : "sleep 30 & echo parent done";

    [Fact]
    public async Task A_command_returns_when_its_own_process_exits()
    {
        using var repo = new FakeRepo();
        var lines = new List<string>();
        var clock = Stopwatch.StartNew();

        var code = await CommandRunner.StreamAsync(
            new ProcessSpec(CommandWithLingeringChild, repo.Path, null),
            (line, _) => { lock (lines) lines.Add(line); },
            CancellationToken.None);

        clock.Stop();

        Assert.Equal(0, code);
        Assert.Contains("parent done", string.Join("\n", lines));

        // The child holds the pipe for 30 seconds; anything near that means we waited for it.
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15),
            $"waited {clock.Elapsed.TotalSeconds:0.0}s for a command whose own process had exited");
    }

    [Fact]
    public async Task An_ordinary_command_still_reports_its_output_and_exit_code()
    {
        using var repo = new FakeRepo();
        var lines = new List<string>();

        var code = await CommandRunner.StreamAsync(
            new ProcessSpec("echo first && echo second", repo.Path, null),
            (line, _) => { lock (lines) lines.Add(line); },
            CancellationToken.None);

        var text = string.Join("\n", lines);
        Assert.Equal(0, code);
        Assert.Contains("first", text);
        Assert.Contains("second", text);
    }

    [Fact]
    public async Task A_failing_command_reports_its_exit_code()
    {
        using var repo = new FakeRepo();

        var code = await CommandRunner.StreamAsync(
            new ProcessSpec(OSKinds.Current == OSKind.Windows ? "exit /b 3" : "exit 3", repo.Path, null),
            (_, _) => { },
            CancellationToken.None);

        Assert.Equal(3, code);
    }
}
