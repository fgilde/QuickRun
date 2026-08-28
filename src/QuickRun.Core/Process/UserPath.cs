using System.Diagnostics;

namespace QuickRun.Core.Process;

/// <summary>
/// The PATH a terminal on this machine would have.
/// <para>
/// An application started from the Finder or a desktop launcher does not inherit the shell's
/// environment: macOS hands it <c>/usr/bin:/bin:/usr/sbin:/sbin</c> and nothing else. Everything a
/// repository needs lives outside that - dotnet in /usr/local/share/dotnet, Docker's client in
/// /usr/local/bin, Homebrew in /opt/homebrew/bin, a Node managed by nvm under the home directory -
/// so QuickRun looked at a machine with all three installed and reported that none of them was.
/// It then installed its own copy of what it could and blocked the run on what it could not.
/// </para>
/// <para>
/// The fix is to be the terminal: ask the user's login shell what its PATH is and adopt it. That
/// covers version managers a fixed list never could, and the fixed list is kept as the fallback for
/// when there is no usable shell to ask.
/// </para>
/// </summary>
public static class UserPath
{
    /// <summary>The marker, so a profile that prints a greeting cannot be mistaken for a PATH.</summary>
    private const string Marker = "QUICKRUN_PATH=";

    /// <summary>
    /// Where tools land when nothing can be asked. Only directories that exist are added, so this
    /// stays a list of places rather than a guess about this machine.
    /// </summary>
    private static IEnumerable<string> Candidates()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        yield return "/opt/homebrew/bin";
        yield return "/opt/homebrew/sbin";
        yield return "/usr/local/bin";
        yield return "/usr/local/sbin";
        yield return "/usr/local/share/dotnet";
        yield return Path.Combine(home, ".dotnet");
        yield return Path.Combine(home, ".dotnet", "tools");
        yield return Path.Combine(home, ".local", "bin");
        yield return "/Applications/Docker.app/Contents/Resources/bin";
    }

    /// <summary>
    /// Extends this process's PATH with the login shell's, so every tool probe and every command a
    /// run executes sees what the user sees. Returns what was added, for the doctor to show.
    /// <para>
    /// Windows is left alone: there a graphical process gets the machine and user PATH already, and
    /// there is no login shell to ask.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> Adopt()
    {
        if (OSKinds.Current == OSKind.Windows) return [];

        var current = Environment.GetEnvironmentVariable("PATH");
        var merged = Merge(current, LoginShellPath(), Candidates().Where(Directory.Exists));

        if (merged == current) return [];

        Environment.SetEnvironmentVariable("PATH", merged);

        var had = Entries(current).ToHashSet(StringComparer.Ordinal);
        return Entries(merged).Where(e => !had.Contains(e)).ToList();
    }

    /// <summary>
    /// The PATH to use: the login shell's order first, because that is the order the user's own
    /// commands resolve in, then whatever this process already had, then the fallbacks.
    /// </summary>
    public static string Merge(string? current, string? login, IEnumerable<string> candidates)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();

        foreach (var entry in Entries(login).Concat(Entries(current)).Concat(candidates))
            if (seen.Add(entry)) result.Add(entry);

        return string.Join(Path.PathSeparator, result);
    }

    /// <summary>The PATH out of a probe's output, ignoring anything a profile printed around it.</summary>
    public static string? ReadProbe(string? output)
    {
        if (string.IsNullOrEmpty(output)) return null;

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(Marker, StringComparison.Ordinal))
                return trimmed[Marker.Length..];
        }

        return null;
    }

    private static IEnumerable<string> Entries(string? path) =>
        (path ?? "").Split(Path.PathSeparator)
            .Select(e => e.Trim())
            .Where(e => e.Length > 0);

    /// <summary>
    /// Asks the login shell what its PATH is, the way a terminal window does. A login shell and not
    /// an interactive one: an interactive shell can wait for input that will never come.
    /// <para>
    /// Public so that a test on a real Unix machine can prove the shell actually answers - the merge
    /// can be checked anywhere, but "the profile was read" cannot be faked.
    /// </para>
    /// </summary>
    public static string? LoginShellPath(string? shell = null, int timeoutMs = 5_000)
    {
        shell ??= Environment.GetEnvironmentVariable("SHELL");
        if (string.IsNullOrWhiteSpace(shell) || !File.Exists(shell)) shell = "/bin/sh";

        try
        {
            var start = new ProcessStartInfo(shell)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            start.ArgumentList.Add("-lc");
            start.ArgumentList.Add($"printf '{Marker}%s\n' \"$PATH\"");

            using var process = System.Diagnostics.Process.Start(start);
            if (process is null) return null;

            // Both streams read as they arrive, and neither with ReadToEnd.
            //
            // A profile that prints - nvm warns on every shell, and plenty of setups print more -
            // fills the pipe buffer, and a full buffer stops the shell dead. Reading one stream to
            // the end while the other fills is a deadlock that no timeout can reach, because the
            // wait never starts. This is startup: a deadlock here means QuickRun never appears.
            var output = new System.Text.StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, _) => { /* drained so the shell can keep going */ };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return null;
            }

            // Flushes what the handlers have not been handed yet.
            process.WaitForExit();

            return ReadProbe(output.ToString());
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException
                                      or IOException or UnauthorizedAccessException)
        {
            // No shell to ask: the fallback list is what is left.
            return null;
        }
    }
}
