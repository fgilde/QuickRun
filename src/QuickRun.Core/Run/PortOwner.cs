using System.Text.RegularExpressions;
using QuickRun.Core.Process;

namespace QuickRun.Core.Run;

/// <summary>
/// Who is listening on a port, so a stop can finish the job.
/// <para>
/// A job object catches everything a command starts - as long as the command's children are started
/// after the job was assigned, and QuickRun can only assign it once the process exists. `npm start`
/// spawns node inside that gap, and when npm then exits, node is an orphan: outside the job, and no
/// longer part of any tree that could be walked. It kept serving on its port while the run said
/// "stopped".
/// </para>
/// <para>
/// So the port itself is asked. Only ports this run opened are ever touched - a port that was
/// already busy before a task started belongs to somebody else, and killing that would be far worse
/// than a leftover.
/// </para>
/// </summary>
public static class PortOwner
{
    /// <summary>The process listening on a loopback port, or null when nothing is.</summary>
    public static int? ListeningPid(int port, Func<string, string[], CommandResult>? capture = null)
    {
        capture ??= (file, args) => CommandRunner.Capture(file, args, timeoutMs: 5_000);

        try
        {
            if (OSKinds.Current == OSKind.Windows)
            {
                var netstat = capture("netstat", ["-ano", "-p", "TCP"]);
                return ParseNetstat(netstat.Output, port);
            }

            // ss first: it is on every current Linux and needs no privileges for one's own
            // processes. lsof is the answer on macOS, and the fallback where ss is missing.
            var ss = capture("ss", ["-ltnpH"]);
            if (ss.ExitCode == 0 && ParseSs(ss.Output, port) is { } found) return found;

            var lsof = capture("lsof", ["-nP", $"-iTCP:{port}", "-sTCP:LISTEN", "-t"]);
            return ParseLsof(lsof.Output);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Not being able to ask is not a reason to fail a stop.
            return null;
        }
    }

    /// <summary>
    /// The listener's pid out of netstat's output.
    /// <para>
    /// The state column is translated - a German Windows says ABHÖREN - so it is not read. What
    /// marks a listener is a local address on this port with no remote peer.
    /// </para>
    /// </summary>
    public static int? ParseNetstat(string? output, int port)
    {
        foreach (var line in Lines(output))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 5 || !fields[0].Equals("TCP", StringComparison.OrdinalIgnoreCase)) continue;

            if (!EndsWithPort(fields[1], port)) continue;
            if (!fields[2].EndsWith(":0", StringComparison.Ordinal)) continue;

            if (int.TryParse(fields[^1], out var pid) && pid > 0) return pid;
        }

        return null;
    }

    private static readonly Regex SsPid = new(@"pid=(\d+)", RegexOptions.Compiled);

    /// <summary>The listener's pid out of `ss -ltnpH`.</summary>
    public static int? ParseSs(string? output, int port)
    {
        foreach (var line in Lines(output))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // LISTEN 0 511 127.0.0.1:3001 0.0.0.0:* users:(("node",pid=1234,fd=20))
            if (!fields.Any(f => EndsWithPort(f, port))) continue;

            if (SsPid.Match(line) is { Success: true } match
                && int.TryParse(match.Groups[1].Value, out var pid) && pid > 0)
                return pid;
        }

        return null;
    }

    /// <summary>The pid out of `lsof -t`, which prints one per line and nothing else.</summary>
    public static int? ParseLsof(string? output)
    {
        foreach (var line in Lines(output))
            if (int.TryParse(line.Trim(), out var pid) && pid > 0)
                return pid;

        return null;
    }

    /// <summary>
    /// Whether an address column names this port. Compared as a port and not as text, so 3001 is
    /// not found in 13001 - a mistake that would kill somebody else's server.
    /// </summary>
    private static bool EndsWithPort(string address, int port)
    {
        var colon = address.LastIndexOf(':');
        if (colon < 0 || colon == address.Length - 1) return false;

        return int.TryParse(address[(colon + 1)..], out var found) && found == port;
    }

    private static IEnumerable<string> Lines(string? output) =>
        (output ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);

    /// <summary>
    /// Ends the process listening on a port, and everything it started. Returns what it did, for a
    /// log line - a stop that killed something has to say so.
    /// </summary>
    public static string? Reclaim(int port, Func<int, int?>? owner = null, Action<int>? kill = null)
    {
        owner ??= p => ListeningPid(p);
        var pid = owner(port);

        if (pid is null) return null;

        // Never this process. A daemon that killed itself while stopping a run would be a
        // spectacular way to fix a leftover.
        if (pid == Environment.ProcessId) return null;

        try
        {
            if (kill is not null) kill(pid.Value);
            else System.Diagnostics.Process.GetProcessById(pid.Value).Kill(entireProcessTree: true);

            return $"port {port} was still held by pid {pid} after the stop - killed it";
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException
                                      or System.ComponentModel.Win32Exception)
        {
            // Gone between asking and killing, or not ours to kill.
            return null;
        }
    }
}
