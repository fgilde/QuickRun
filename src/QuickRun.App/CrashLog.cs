using QuickRun.Core;
using QuickRun.Core.Workspace;

namespace QuickRun.App;

/// <summary>
/// Writes down what killed the process.
/// <para>
/// An exception on the interface thread takes the whole application with it and there is nothing to
/// catch it: the frame that threw belongs to the UI framework, not to us. That happened - a
/// malformed icon file, and the first right-click on the tray menu ended everything - and the only
/// trace anywhere was an entry in the Windows event log. Nobody debugs from there, and a user on
/// another machine cannot be asked to.
/// </para>
/// </summary>
public static class CrashLog
{
    /// <summary>How many crash files are kept before the oldest is dropped.</summary>
    private const int Keep = 20;

    private static string Directory => Path.Combine(new WorkspaceStore().Root, "crashes");

    /// <summary>Installs the handlers. Called once, before anything else can throw.</summary>
    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write("unhandled", e.ExceptionObject as Exception);

        // A faulted task nobody awaited. Not fatal by default since .NET 4.5, but it is exactly how
        // a background failure disappears without a word.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("unobserved", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>The file that was written, or null when even that failed.</summary>
    public static string? Write(string kind, Exception? exception)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);

            var path = Path.Combine(Directory,
                $"{DateTimeOffset.Now:yyyy-MM-dd-HHmmss}-{kind}.log");

            File.WriteAllText(path, string.Join(Environment.NewLine,
                $"QuickRun {BuildInfo.Version} on {OSKinds.Current.Key()}",
                $"{DateTimeOffset.Now:O}",
                $"command line: {Environment.CommandLine}",
                "",
                exception?.ToString() ?? "no exception object was given",
                ""));

            Trim();

            // On the way out, so a terminal says where to look rather than nothing at all.
            Console.Error.WriteLine($"quickrun crashed - written to {path}");
            return path;
        }
        catch (Exception)
        {
            // Failing to record a crash must not itself be one.
            return null;
        }
    }

    /// <summary>The most recent crash, when there is one: how many, when, and what it said.</summary>
    public static (int Count, DateTimeOffset When, string Summary, string Path)? Newest()
    {
        try
        {
            if (!System.IO.Directory.Exists(Directory)) return null;

            var files = new DirectoryInfo(Directory).GetFiles("*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            if (files.Count == 0) return null;

            // The exception line, which is the one worth repeating - the header above it is ours.
            var summary = File.ReadLines(files[0].FullName)
                .FirstOrDefault(line => line.Contains("Exception", StringComparison.Ordinal))
                ?? "no exception line in the file";

            return (files.Count, files[0].LastWriteTime, summary.Trim(), files[0].FullName);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void Trim()
    {
        var files = new DirectoryInfo(Directory).GetFiles("*.log")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Skip(Keep);

        foreach (var file in files)
        {
            try { file.Delete(); }
            catch (IOException) { /* someone is reading it */ }
        }
    }
}
