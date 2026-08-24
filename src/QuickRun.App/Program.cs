using QuickRun.App;
using QuickRun.App.Commands;
using Spectre.Console.Cli;

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("quickrun");

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
