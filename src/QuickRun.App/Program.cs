using QuickRun.App;
using QuickRun.App.Commands;
using QuickRun.Core;
using Spectre.Console.Cli;

// Before anything writes: bind to the parent console if this was started from a terminal.
ConsoleAttach.TryAttach();

// Double-clicking the binary must do something useful. Printing CLI help into a console window
// that closes again is the worst possible front door, so no command means: start the daemon, put
// an icon in the tray and open the dashboard.
//
// Options without a command take the same path - "quickrun --port 9999" is plainly a request to
// start, not a usage error.
string[] globalFlags = ["-h", "--help", "-v", "--version"];
if (args.Length == 0 || (args[0].StartsWith('-') && !globalFlags.Contains(args[0], StringComparer.Ordinal)))
    args = ["ui", .. args];

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

try
{
    return app.Run(args);
}
catch (Exception e)
{
    Output.Error(e.Message);
    return 2;
}
