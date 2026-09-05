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
    /// <param name="onStarted">
    /// The process id, as soon as there is one. A caller that wants to do something with the tree
    /// the command starts - raise its window, say - has no other way to find it.
    /// </param>
    /// <param name="group">
    /// The run's process group. Passing the run's own group is what lets a stop reach a server a
    /// task launched in the background before exiting: the group outlives this command. Without one,
    /// a group is made for this command alone.
    /// </param>
    public static async Task<int> StreamAsync(ProcessSpec spec, Action<string, bool> onLine,
        CancellationToken ct, Action<int>? onStarted = null, ProcessGroup? group = null)
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
            // Everything this command starts, killable as a unit: a stop that only kills the tree
            // leaves behind whatever was re-parented on the way, which is how a stopped run kept
            // answering on its port.
            var owned = group is null ? ProcessGroup.Create() : null;
            var members = group ?? owned!;
            members.Add(process);

            onStarted?.Invoke(process.Id);

            // Waiting on the process itself, not on its pipes. WaitForExitAsync also waits for the
            // redirected streams to reach end of file, and a command that leaves a background child
            // holding those handles - MSBuild's reusable worker nodes are the usual one - never
            // gives them up. That looked exactly like a run frozen after `dotnet restore`.
            process.EnableRaisingEvents = true;

            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            process.Exited += (_, _) => finished.TrySetResult();
            if (process.HasExited) finished.TrySetResult();

            var reading = Task.WhenAll(
                PumpAsync(process.StandardOutput, line => onLine(line, false)),
                PumpAsync(process.StandardError, line => onLine(line, true)));

            // Said out loud, because "stop did not stop" is otherwise unfalsifiable from a log.
            await using var registration = ct.Register(() =>
            {
                onLine($"stopping - killing pid {process.Id} and everything it started"
                       + (members.Grouped ? "" : " (tree only: no job object)"), false);
                members.Terminate();
            });
            await finished.Task;

            // The last lines are still in flight, so the output gets a moment to arrive - but only
            // a moment, because whatever is still holding the pipe is not this process any more.
            await Task.WhenAny(reading, Task.Delay(OutputDrain, CancellationToken.None));

            // A group made here belongs to this command only, so it goes when the command does. The
            // run's own group is not disposed here: what the command left behind is still in it.
            owned?.Dispose();

            return process.ExitCode;
        }
    }

    /// <summary>How long output is still collected after the process itself has gone.</summary>
    private static readonly TimeSpan OutputDrain = TimeSpan.FromSeconds(2);

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
            splitter.Push(new string(buffer, 0, read), line => onLine(Decode(line)));
        }

        splitter.Flush(line => onLine(Decode(line)));
    }

    /// <summary>
    /// A line of console output, as text.
    /// <para>
    /// On Windows the two encodings in play disagree: node, git and the .NET tools write UTF-8,
    /// while a redirected stream is read in the console's own code page, which turned "LX Family
    /// läuft" into "LX Family lÃ¤uft" in the log. The streams are therefore read as Latin-1, which
    /// maps every byte to exactly one character and so loses nothing, and each line is decoded here:
    /// UTF-8 when the bytes are valid UTF-8, and left alone when they are not - so a message from
    /// cmd.exe in the machine's own code page still reads correctly.
    /// </para>
    /// </summary>
    private static string Decode(string line)
    {
        if (!OperatingSystem.IsWindows() || Ascii.IsValid(line)) return line;

        try
        {
            return Strict.GetString(Encoding.Latin1.GetBytes(line));
        }
        catch (DecoderFallbackException)
        {
            // Not UTF-8. That leaves the console's own code page, which .NET cannot produce without
            // the code-page provider package, so the bytes are left as they are: a line from a
            // localised cmd.exe still looks the way it always did, rather than becoming a row of
            // replacement characters.
            return line;
        }
    }

    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);


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

        // Byte-preserving on Windows, so Decode above can tell UTF-8 from the console code page
        // rather than being handed characters that were already guessed at.
        if (OperatingSystem.IsWindows())
        {
            psi.StandardOutputEncoding = Encoding.Latin1;
            psi.StandardErrorEncoding = Encoding.Latin1;
        }

        // cmd.exe is handed its line whole; everything else gets an argument list, which is the
        // safer form because .NET does the quoting. See ShellCommand.RawCommandLine for why cmd is
        // the exception - in short, it does not read \" as a quote and passes it on.
        var list = args as IReadOnlyList<string> ?? args.ToList();

        if (ShellCommand.RawCommandLine(file, list) is { } raw) psi.Arguments = raw;
        else foreach (var a in list) psi.ArgumentList.Add(a);

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
