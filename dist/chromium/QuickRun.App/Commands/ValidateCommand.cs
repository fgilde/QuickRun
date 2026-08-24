using System.ComponentModel;
using QuickRun.Core;
using QuickRun.Core.Config;
using Spectre.Console.Cli;

namespace QuickRun.App.Commands;

public sealed record ValidateResult(int ExitCode, IReadOnlyList<ValidationIssue> Issues, RunConfig? Config);

/// <summary>
/// Validates a local config without running anything, so repository owners can use it as a
/// pre-commit check. The logic lives in <see cref="Check"/> so it is testable without capturing
/// console output.
/// </summary>
public sealed class ValidateCommand : Command<ValidateCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[path]")]
        [Description("Directory containing quickrun.yml, or the file itself. Defaults to the current directory.")]
        public string? Path { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken) =>
        Check(settings.Path ?? Environment.CurrentDirectory, quiet: false).ExitCode;

    public static ValidateResult Check(string path, bool quiet)
    {
        var file = File.Exists(path) ? path : ConfigParser.FindConfigFile(path);
        if (file is null)
        {
            var missing = new ValidationIssue("", $"no quickrun.yml found in {path}", true);
            if (!quiet) Output.Error(missing.Message);
            return new(2, new[] { missing }, null);
        }

        RunConfig config;
        try
        {
            config = ConfigParser.Parse(File.ReadAllText(file), OSKinds.Current);
        }
        catch (ConfigException e)
        {
            var issue = new ValidationIssue(file, e.Message, true);
            if (!quiet) Output.Issues(new[] { issue });
            return new(1, new[] { issue }, null);
        }

        var issues = ConfigValidator.Validate(config);
        if (!quiet)
        {
            if (issues.Count == 0) Output.Info($"{file} is valid");
            else Output.Issues(issues);
        }

        return new(issues.Any(i => i.IsError) ? 1 : 0, issues, config);
    }
}
