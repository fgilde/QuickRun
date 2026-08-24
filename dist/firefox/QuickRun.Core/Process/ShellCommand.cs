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
}
