using QuickRun.Core.Process;
using SysProcess = System.Diagnostics.Process;

namespace QuickRun.Core.Tests;

/// <summary>
/// Stopping has to reach what the command started, including what it lost track of.
/// <para>
/// The case that failed in the wild: a task started an application, the process in between exited,
/// and the application was left with a parent that no longer exists. Killing the process tree walks
/// the parent chain, so it never found it - the run said "stopped" while the app kept serving on
/// its port.
/// </para>
/// </summary>
public class ProcessGroupTests
{
    [Fact]
    public async Task Cancelling_kills_a_process_the_command_orphaned()
    {
        // The job object is the Windows mechanism for this. Elsewhere stopping still kills the tree,
        // which is what .NET offers, so there is nothing here to assert.
        if (!OperatingSystem.IsWindows()) return;

        var directory = Directory.CreateTempSubdirectory("quickrun-group");

        try
        {
            var pidFile = Path.Combine(directory.FullName, "child.pid");

            // Starts a long-lived process and exits immediately, leaving it with a dead parent.
            File.WriteAllText(Path.Combine(directory.FullName, "orphan.ps1"), """
                $child = Start-Process powershell -PassThru -WindowStyle Hidden `
                  -ArgumentList '-NoProfile', '-Command', 'Start-Sleep -Seconds 120'
                Set-Content -Path (Join-Path $PSScriptRoot 'child.pid') -Value $child.Id
                """);

            // And the command itself stays alive, so this tests stopping rather than finishing.
            File.WriteAllText(Path.Combine(directory.FullName, "parent.cmd"), """
                @echo off
                powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0orphan.ps1"
                powershell -NoProfile -Command "Start-Sleep -Seconds 120"
                """);

            using var cts = new CancellationTokenSource();
            // The full path: cmd does not look in the working directory for a command name.
            var script = Path.Combine(directory.FullName, "parent.cmd");
            var spec = new ProcessSpec(script, directory.FullName, null);
            var output = new List<string>();
            var running = CommandRunner.StreamAsync(spec,
                (line, _) => { lock (output) output.Add(line); }, cts.Token);

            var orphan = await WaitForPidAsync(pidFile, TimeSpan.FromSeconds(60));
            lock (output)
                Assert.True(orphan > 0, "the orphaned process never started: " + string.Join(" | ", output));

            // Proven to discriminate: with the job object skipped, this same test reports the
            // orphan surviving the stop - which is the bug it exists for.
            Assert.True(Alive(orphan), "the orphaned process was not running");

            await cts.CancelAsync();
            await running;

            Assert.False(await StaysAliveAsync(orphan, TimeSpan.FromSeconds(10)),
                $"process {orphan} survived the stop");
        }
        finally
        {
            try { directory.Delete(recursive: true); } catch (IOException) { }
        }
    }

    private static async Task<int> WaitForPidAsync(string file, TimeSpan patience)
    {
        var deadline = DateTime.UtcNow + patience;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (File.Exists(file) && int.TryParse(File.ReadAllText(file).Trim(), out var pid)) return pid;
            }
            catch (IOException)
            {
                // Being written right now.
            }

            await Task.Delay(200);
        }

        return 0;
    }

    private static async Task<bool> StaysAliveAsync(int pid, TimeSpan patience)
    {
        var deadline = DateTime.UtcNow + patience;

        while (DateTime.UtcNow < deadline)
        {
            if (!Alive(pid)) return false;
            await Task.Delay(200);
        }

        return Alive(pid);
    }

    private static bool Alive(int pid)
    {
        try
        {
            using var process = SysProcess.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
