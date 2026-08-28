using System.ComponentModel;
using QuickRun.App.Daemon;
using QuickRun.Core;
using Spectre.Console.Cli;

namespace QuickRun.App.Commands;

/// <summary>
/// Hands a folder to QuickRun and puts the plan on screen.
/// <para>
/// This is what a shell verb calls - "Run with QuickRun" on a folder or on a quickrun.yml. It runs
/// nothing itself and it is not the instance: it asks the one that is running, starting it first if
/// it has to, and then gets out of the way. That matters because the run has to outlive the click:
/// a process the shell started would take the run with it when its window closed, and the binary is
/// built for the GUI subsystem, so a confirmation prompt in a console nobody can see is no
/// confirmation at all.
/// </para>
/// </summary>
public sealed class OpenCommand : AsyncCommand<OpenCommand.Settings>
{
    /// <summary>How long a QuickRun that had to be started gets to answer.</summary>
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("The folder to run, or a quickrun.yml inside it.")]
        public string Path { get; init; } = "";

        [CommandOption("--copy")]
        [Description("Run a copy under the workspace directory, leaving the folder alone.")]
        public bool Copy { get; init; }

        [CommandOption("-p|--port")]
        [Description("Port QuickRun listens on.")]
        public int Port { get; init; } = DaemonHost.DefaultPort;
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (Folder(settings.Path) is not { } folder)
        {
            Output.Error($"'{settings.Path}' is not a folder, and not a file inside one");
            return 2;
        }

        if (!await Reachable(settings.Port, cancellationToken)) return 1;

        var (shown, error) = await SingleInstance.LocalAsync(
            settings.Port, folder, settings.Copy, cancellationToken);

        // A config that will not load, or a folder with nothing to run: still worth showing, because
        // the window is where the reason is readable and where a config can be written.
        if (error is not null) Output.Warn(error);

        if (!shown)
        {
            Output.Error("QuickRun is running without a window, so there is nowhere to show the plan"
                         + $" - run it here instead: quickrun run --path \"{folder}\"");
            return 1;
        }

        Output.Info($"{folder} is waiting for confirmation in QuickRun");
        return 0;
    }

    /// <summary>
    /// The folder to run. A shell verb on a file passes the file, so a quickrun.yml means the
    /// directory that holds it - which is the only sense in which a config can be "run".
    /// </summary>
    private static string? Folder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            var full = System.IO.Path.GetFullPath(path);

            if (Directory.Exists(full)) return full;
            if (File.Exists(full)) return System.IO.Path.GetDirectoryName(full);

            return null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>Makes sure there is a QuickRun to talk to, starting one if there is not.</summary>
    private static async Task<bool> Reachable(int port, CancellationToken ct)
    {
        if (await SingleInstance.RunningAsync(port, ct)) return true;

        Output.Info("starting QuickRun…");
        if (!SingleInstance.Start(port)) return false;

        if (await SingleInstance.WaitAsync(port, StartTimeout, ct)) return true;

        Output.Error($"QuickRun did not answer on port {port} within {StartTimeout.TotalSeconds:0}s");
        return false;
    }
}
