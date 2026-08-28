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
    /// <summary>How long the lingering child keeps the handle. Long enough to be unmistakable.</summary>
    private const int ChildSeconds = 30;

    /// <summary>
    /// A command that leaves a background child holding the output handle - and whose child writes a
    /// file when it finally goes, so its presence is a fact rather than a duration.
    /// </summary>
    private static string CommandWithLingeringChild(string marker) =>
        OSKinds.Current == OSKind.Windows
            ? "powershell -NoProfile -Command \"Start-Process -NoNewWindow powershell -ArgumentList "
              + $"'-NoProfile','-Command','Start-Sleep -Seconds {ChildSeconds}; "
              + $"Set-Content -LiteralPath \"\"{marker}\"\" -Value done'; 'parent done'\""
            : $"( sleep {ChildSeconds}; touch '{marker}' ) & echo parent done";

    /// <summary>
    /// Returns when its own process is gone, rather than when the pipes close.
    /// <para>
    /// Proven by the child and not by a clock: the child writes a file when it finishes, so a return
    /// with that file absent is a return that did not wait for it. Timing this instead measured how
    /// long a loaded build agent takes to start two PowerShells - it failed in CI at 21 seconds
    /// while taking 0.2 here.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_command_returns_when_its_own_process_exits()
    {
        using var repo = new FakeRepo();
        var marker = Path.Combine(repo.Path, "child-finished.txt");
        var lines = new List<string>();

        var code = await CommandRunner.StreamAsync(
            new ProcessSpec(CommandWithLingeringChild(marker), repo.Path, null),
            (line, _) => { lock (lines) lines.Add(line); },
            CancellationToken.None);

        Assert.Equal(0, code);
        Assert.Contains("parent done", string.Join("\n", lines));

        // The child still holds the handle: it has not written its file yet.
        Assert.False(File.Exists(marker),
            "the command waited for a background child that had inherited its output handle");
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
