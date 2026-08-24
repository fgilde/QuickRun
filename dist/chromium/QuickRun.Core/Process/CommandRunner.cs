using System.Diagnostics;
using System.Text;
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

    /// <summary>
    /// Runs a process directly (no shell), forwarding every line as it arrives and also returning
    /// the full output. Splits on CR as well as LF, so git's progress overwrites are seen live.
    /// </summary>
    public static CommandResult StreamCapture(string file, IEnumerable<string> args, string? cwd,
        IReadOnlyDictionary<string, string>? env, Action<string, bool> onLine, int timeoutMs = 300_000)
    {
        var psi = Info(file, args, cwd, env);

        SysProcess? process;
        try { process = SysProcess.Start(psi); }
        catch (Exception e) { return new(-1, e.Message, false); }

        if (process is null) return new(-1, $"could not start {file}", false);

        using (process)
        {
            var collected = new StringBuilder();

            void Collect(string line, bool isError)
            {
                lock (collected) collected.AppendLine(line);
                onLine(line, isError);
            }

            var readers = Task.WhenAll(
                PumpAsync(process.StandardOutput, line => Collect(line, false)),
                PumpAsync(process.StandardError, line => Collect(line, true)));

            if (!process.WaitForExit(timeoutMs))
            {
                Kill(process);
                return new(-1, $"{file} timed out after {timeoutMs} ms", true);
            }

            readers.Wait(TimeSpan.FromSeconds(5));
            lock (collected) return new(process.ExitCode, collected.ToString().Trim(), false);
        }
    }

    private static async Task PumpAsync(StreamReader reader, Action<string> onLine)
    {
        var splitter = new LineSplitter();
        var buffer = new char[1024];

        while (true)
        {
            var read = await reader.ReadAsync(buffer, 0, buffer.Length);
            if (read == 0) break;
            splitter.Push(new string(buffer, 0, read), onLine);
        }

        splitter.Flush(onLine);
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
