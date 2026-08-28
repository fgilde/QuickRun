using QuickRun.App;
using QuickRun.App.Commands;
using QuickRun.App.Ui;
using QuickRun.Core;
using Spectre.Console.Cli;

/// <summary>
/// The entry point is written out rather than left as top-level statements for one reason:
/// <c>[STAThread]</c>. The desktop window hosts the local web UI in the system WebView, and WebView2
/// refuses to start on a thread in the multi-threaded apartment - and on .NET the apartment of the
/// main thread is fixed before any code runs, so it can only be declared here.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Before anything writes: bind to the parent console if this was started from a terminal.
        ConsoleAttach.TryAttach();

        // And before anything can throw. An exception on the interface thread ends the process from
        // inside the UI framework, where there is no frame of ours to catch it - so the only way to
        // know what happened afterwards is to have written it down as it went.
        CrashLog.Install();

        // macOS may only be drawn from the thread the process started on, so there the command runs
        // on a worker and this thread waits to be handed the interface loop. Windows wants the
        // opposite - a single-threaded-apartment thread of its own for the WebView - and gets it
        // when the loop is actually needed.
        return UiHost.WantsFirstThread ? UiHost.Own(() => Execute(args)) : Execute(args);
    }

    private static int Execute(string[] args)
    {

        // Double-clicking the binary must do something useful. Printing CLI help into a console window
        // that closes again is the worst possible front door, so no command means: start the daemon, put
        // an icon in the tray and open the dashboard.
        //
        // Options without a command take the same path - "quickrun --port 9999" is plainly a request to
        // start, not a usage error.
        string[] globalFlags = ["-h", "--help", "-v", "--version"];
        if (args.Length == 0 || (args[0].StartsWith('-') && !globalFlags.Contains(args[0], StringComparer.Ordinal)))
            args = ["ui", .. args];

        var app = Build();

        try
        {
            return app.Run(args);
        }
        catch (Exception e)
        {
            Output.Error(e.Message);
            return 2;
        }
    }

    /// <summary>
    /// The command line as it is configured. Separate so a test can hold it: a command that is
    /// registered wrongly - a settings type the binder cannot construct, an option declared twice -
    /// only shows up when someone runs that one command, and that is not the moment to find out.
    /// </summary>
    internal static CommandApp Build()
    {
        var app = new CommandApp();

        app.Configure(config =>
        {
            config.SetApplicationName("quickrun");
            config.SetApplicationVersion(BuildInfo.Version);

            config.AddCommand<RunCommand>("run")
                .WithDescription("Check out a repository and run it.")
                .WithExample("run", "acme/app")
                .WithExample("run", "acme/app", "--ref", "feature/login")
                .WithExample("run", "https://github.com/acme/app", "--pr", "42", "--input", "apiKey=sk-1");

            config.AddCommand<ValidateCommand>("validate")
                .WithDescription("Validate a quickrun.yml without running anything.")
                .WithExample("validate")
                .WithExample("validate", "./my-repo");

            config.AddCommand<DetectCommand>("detect")
                .WithDescription("Show how QuickRun would start a repository that has no config.")
                .WithExample("detect")
                .WithExample("detect", ".", "--save");

            config.AddCommand<UiCommand>("ui")
                .WithDescription("Start QuickRun with a tray icon and open the dashboard. The default.")
                .WithExample("ui")
                .WithExample("ui", "--no-browser");

            config.AddCommand<DaemonCommand>("daemon")
                .WithDescription("Run the localhost listener the browser extension talks to.")
                .WithExample("daemon")
                .WithExample("daemon");

            config.AddCommand<InstallCommand>("install")
                .WithDescription("Register quickrun:// and start the daemon at login.");

            config.AddCommand<UninstallCommand>("uninstall")
                .WithDescription("Undo what install did. Workspaces are kept.");

            config.AddCommand<HandleCommand>("handle")
                .IsHidden()
                .WithDescription("Handle a quickrun:// URL. Invoked by the operating system.");


            config.AddCommand<OpenCommand>("open")
                .WithDescription("Hand a folder to QuickRun and show the plan. What a shell verb calls.")
                .WithExample("open", ".")
                .WithExample("open", "~/dev/planner", "--copy");

            config.AddCommand<DoctorCommand>("doctor")
                .WithDescription("Check that this installation works: listener, extension contract, "
                                 + "workspace, tray icon, window.")
                .WithExample("doctor")
                .WithExample("doctor", "--no-ui");

            config.AddCommand<UpdateCommand>("update")
                .WithDescription("Check for a newer QuickRun and install it when QuickRun owns the binary.")
                .WithExample("update")
                .WithExample("update", "--check");

            config.AddCommand<ListCommand>("ls")
                .WithDescription("List checked-out workspaces with their size and last use.");

            config.AddCommand<CleanCommand>("clean")
                .WithDescription("Remove workspaces.")
                .WithExample("clean", "--all")
                .WithExample("clean", "--older-than", "30d")
                .WithExample("clean", "acme__app__main-1a2b3c");

            // One handler, so a stack trace never reaches a user while the message still does.
            config.PropagateExceptions();
        });

        return app;
    }
}
