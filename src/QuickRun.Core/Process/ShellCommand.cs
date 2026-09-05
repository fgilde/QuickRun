namespace QuickRun.Core.Process;

public static class ShellCommand
{
    private static readonly string[] GitBashCandidates =
    {
        @"C:\Program Files\Git\bin\bash.exe",
        @"C:\Program Files (x86)\Git\bin\bash.exe",
    };

    /// <summary>
    /// Picks the shell for a command line. On Windows a <c>.sh</c> entry point is routed through
    /// Git for Windows' bash when available, so a repository shipping only run.sh still works there.
    /// </summary>
    public static (string File, string[] Args) Resolve(string command, OSKind os, Func<string, bool> fileExists)
    {
        if (os != OSKind.Windows) return ("/bin/sh", new[] { "-c", command });

        var first = command.TrimStart().Split(' ', 2)[0];
        if (first.EndsWith(".sh", StringComparison.OrdinalIgnoreCase))
        {
            var bash = GitBashCandidates.FirstOrDefault(fileExists);
            if (bash is not null) return (bash, new[] { "-c", command });
        }

        return ("cmd.exe", new[] { "/c", command });
    }

    public static (string File, string[] Args) Resolve(string command) =>
        Resolve(command, OSKinds.Current, File.Exists);

    /// <summary>
    /// The command line to hand cmd.exe as one raw string, or null when an argument list is right.
    /// <para>
    /// .NET builds a command line out of an argument list by escaping each argument the way the C
    /// runtime parses them - a quote becomes <c>\"</c>. Every program that parses its arguments that
    /// way puts the original string back together, which is why bash and git are fine. cmd.exe does
    /// not: it has rules of its own, does not know <c>\"</c>, and passes it through. So
    /// <c>-e PASSWORD="secret"</c> arrived at docker as <c>-e PASSWORD=\"secret\"</c> and the
    /// password became <c>"secret"</c>, quotes and all - a database whose credentials silently
    /// contain punctuation nobody typed, and an application next to it that cannot log in.
    /// </para>
    /// <para>
    /// <c>/s</c> is what makes this exact: with it, cmd strips the first and last character when
    /// both are quotes and takes everything between them verbatim. Without it, whether the outer
    /// quotes are stripped depends on what else is in the line.
    /// </para>
    /// </summary>
    public static string? RawCommandLine(string file, IReadOnlyList<string> args) =>
        file.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase) && args is ["/c", var command]
            ? $"/s /c \"{command}\""
            : null;
}
