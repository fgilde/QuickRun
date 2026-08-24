using System.Diagnostics;
using SysProcess = System.Diagnostics.Process;

namespace QuickRun.Core.Process;

public sealed record CommandResult(int ExitCode, string Output, bool TimedOut);

public sealed record ProcessSpec(string Command, string? Cwd, IReadOnlyDictionary<string, string>? Env);

public static class CommandRunner
{
    /// <summary>Runs a process to completion and returns its combined output.</summary>
    public static CommandResult Capture(string file, IEnumerable<string> args, string? cwd = null,
        IReadOnlyDictionary<string, string>? env = null, int timeoutMs = 120_000)
    {
        var psi = Info(file, args, cwd, env);
        try
        {
            using var process = SysProcess.Start(psi);
            if (process is null) return new(-1, $"could not start {file}", false);

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(timeoutMs))
            {
                Kill(process);
                return new(-1, $"{file} timed out after {timeoutMs} ms", true);
            }

            return new(process.ExitCode, (stdout.Result + stderr.Result).Trim(), false);
        }
        catch (Exception e)
        {
            return new(-1, e.Message, false);
        }
    }

    /// <summary>
    /// Runs a command line through the platform shell, forwarding each output line as it arrives.
    /// Cancellation kills the whole process tree.
    /// </summary>
    public static async Task<int> StreamAsync(ProcessSpec spec, Action<string, bool> onLine, CancellationToken ct)
    {
        var (file, args) = ShellCommand.Resolve(spec.Command);
        var psi = Info(file, args, spec.Cwd, spec.Env);

        SysProcess? process;
        try
        {
            process = SysProcess.Start(psi);
        }
        catch (Exception e)
        {
            onLine(e.Message, true);
            return -1;
        }

        if (process is null)
        {
            onLine($"could not start: {spec.Command}", true);
            return -1;
        }

        using (process)
        {
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data, false); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data, true); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await using var registration = ct.Register(() => Kill(process));
            await process.WaitForExitAsync(CancellationToken.None);
            return process.ExitCode;
        }
    }

    private static ProcessStartInfo Info(string file, IEnumerable<string> args, string? cwd,
        IReadOnlyDictionary<string, string>? env)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = cwd ?? Environment.CurrentDirectory,
        };

        foreach (var a in args) psi.ArgumentList.Add(a);

        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        if (env is not null)
            foreach (var kv in env) psi.Environment[kv.Key] = kv.Value;

        return psi;
    }

    private static void Kill(SysProcess process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // the process exited between the check and the kill, or we lost the right to signal it
        }
    }
}
