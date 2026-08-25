using System.ComponentModel;
using QuickRun.Core;
using QuickRun.Core.Config;
using QuickRun.Core.Detect;
using QuickRun.Core.Foreign;
using Spectre.Console.Cli;

namespace QuickRun.App.Commands;

public sealed record DetectResult(int ExitCode, IReadOnlyList<Candidate> Candidates, string? Error);

/// <summary>Shows how QuickRun would start a repository that has no config, and can write one.</summary>
public sealed class DetectCommand : Command<DetectCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[path]")]
        [Description("Directory to scan. Defaults to the current directory.")]
        public string? Path { get; init; }

        [CommandOption("--save")]
        [Description("Write the highest-ranked candidate to quickrun.yml. Never overwrites an existing file.")]
        public bool Save { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var path = settings.Path ?? Environment.CurrentDirectory;
        var result = Find(path);

        if (result.Error is { } error)
        {
            Output.Error(error);
            return result.ExitCode;
        }

        Output.Candidates(result.Candidates);

        if (!settings.Save) return 0;

        var saved = Save(path, result.Candidates[0]);
        if (saved.Error is { } saveError) Output.Error(saveError);
        else Output.Info($"wrote {saved.Path}");
        return saved.ExitCode;
    }

    public static DetectResult Find(string path)
    {
        if (!Directory.Exists(path))
            return new(2, Array.Empty<Candidate>(), $"no such directory: {path}");

        if (ConfigParser.FindConfigFile(path) is { } existing)
            Output.Info($"note: {existing} already exists, detection is only informational here");

        var candidates = Detector.Detect(path, OSKinds.Current).ToList();

        // A repository written for another launcher already says how it starts, so it belongs at
        // the top of the list rather than below whatever guessing found.
        if (Pinokio.Load(path, OSKinds.Current) is { } foreign)
            candidates.Insert(0, Foreign(foreign));

        return candidates.Count == 0
            ? new(1, candidates, $"nothing detectable in {path}")
            : new(0, candidates, null);
    }

    private static Candidate Foreign(ForeignConfig foreign) => new(
        foreign.Kind,
        $"{foreign.Kind} scripts in the repository ({foreign.Config.Tasks.Count} task(s))",
        "",
        foreign.Config.Setup.Select(s => s.Run).ToList(),
        foreign.Config.Tasks.Select(t => t.Run).ToList(),
        98);

    public static (int ExitCode, string? Path, string? Error) Save(string directory, Candidate candidate)
    {
        var target = Path.Combine(directory, ConfigParser.FileNames[0]);
        if (File.Exists(target))
            return (1, null, $"{target} already exists - not overwriting a config someone wrote by hand");

        var name = new DirectoryInfo(Path.GetFullPath(directory)).Name;
        File.WriteAllText(target, Detector.ToYaml(candidate, name));
        return (0, target, null);
    }
}
