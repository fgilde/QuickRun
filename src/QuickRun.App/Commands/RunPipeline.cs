using QuickRun.Core;
using QuickRun.Core.Config;
using QuickRun.Core.Detect;
using QuickRun.Core.Foreign;
using QuickRun.Core.Git;
using QuickRun.Core.Inputs;
using QuickRun.Core.Run;
using QuickRun.Core.Workspace;

namespace QuickRun.App.Commands;

public sealed record RunArgs(
    string Repo,
    string? Ref,
    int? PullRequest,
    string? Subdir,
    IReadOnlyList<string> Inputs,
    string? Token,
    bool Fresh,
    bool Yes,
    bool NoOpen,
    string? ConfigPath,
    /// <summary>
    /// A config supplied by the caller rather than read from anywhere. This is what the builder
    /// tests with: the config being written must win, even over the repository's own.
    /// </summary>
    string? ConfigText = null);

public sealed record RunPreparation(
    int ExitCode,
    RunPlan? Plan,
    RunConfig? Config,
    string? Workspace,
    IReadOnlyDictionary<string, string?>? Values,
    string? Error,
    IReadOnlyList<Candidate> OtherCandidates,
    IReadOnlyList<string> Notes,
    /// <summary>
    /// Where the instructions came from. A plan a repository committed and a plan QuickRun worked
    /// out by reading the files deserve different amounts of trust, and a reader can only weigh
    /// that if they are told which one is in front of them.
    /// </summary>
    ConfigOrigin Origin = ConfigOrigin.Repository);

/// <summary>Where a run's instructions came from.</summary>
public enum ConfigOrigin
{
    /// <summary>A quickrun.yml committed to the repository.</summary>
    Repository,

    /// <summary>A config saved on this machine for this repository, which wins over the above.</summary>
    Local,

    /// <summary>A config handed in for this run - from the editor, or --config-text.</summary>
    Supplied,

    /// <summary>A file named with --config.</summary>
    Explicit,

    /// <summary>Scripts written for another launcher. Pinokio, so far.</summary>
    Foreign,

    /// <summary>Nothing to go on, so QuickRun read the repository and decided for itself.</summary>
    Detected,
}

/// <summary>
/// Everything up to but not including execution: normalise, check out, load or detect a config,
/// collect inputs, build the plan. Never prints, never executes - so it is testable end to end
/// against a local repository.
/// </summary>
public static class RunPipeline
{
    private const string ConfigDocs = "https://fgilde.github.io/QuickRun/docs/config";

    /// <param name="collectInputs">
    /// How to fill in missing values. The CLI passes a console prompt; --yes and tests pass a
    /// function that returns what it was given, so a missing required input fails instead of
    /// blocking on a prompt nobody can answer.
    /// </param>
    public static RunPreparation Prepare(
        RunArgs args,
        WorkspaceStore store,
        GitClient git,
        Func<IReadOnlyList<InputDef>, IReadOnlyDictionary<string, string?>, IReadOnlyDictionary<string, string?>> collectInputs)
    {
        string repo;
        try { repo = Normalize(args.Repo); }
        catch (ArgumentException e) { return Usage(e.Message); }

        IReadOnlyDictionary<string, string?> provided;
        try { provided = InputResolver.ParseAssignments(args.Inputs); }
        catch (ArgumentException e) { return Usage(e.Message); }

        var reference = args.Ref ?? DefaultRef(git, repo);
        var workspace = store.PathFor(repo, reference);

        var checkout = git.CheckoutOrUpdate(repo, reference, args.PullRequest, workspace, args.Fresh);
        if (!checkout.Ok) return Failed(checkout.Error ?? "checkout failed");

        store.Touch(WorkspaceStore.IdFor(repo, reference), repo, reference, checkout.Commit, null);

        string root;
        try { root = ResolveRoot(workspace, args.Subdir); }
        catch (ArgumentException e) { return Usage(e.Message); }
        if (!Directory.Exists(root)) return Failed($"subdir '{args.Subdir}' does not exist in {repo}");

        var loaded = LoadConfig(root, args, repo, new ConfigOverrides(store.Root));
        if (loaded.Error is { } loadError) return Failed(loadError);

        var config = loaded.Config!;
        var issues = ConfigValidator.Validate(config);
        if (issues.Any(i => i.IsError))
            return Failed(string.Join("\n", issues.Where(i => i.IsError).Select(Describe)));

        var values = InputResolver.ApplyDefaults(config.Inputs, provided);
        var errors = InputResolver.Validate(config.Inputs, values);
        if (errors.Count > 0)
        {
            values = InputResolver.ApplyDefaults(config.Inputs, collectInputs(config.Inputs, values));
            errors = InputResolver.Validate(config.Inputs, values);
            if (errors.Count > 0)
                // Not a dead end: the caller may be a window that can ask for the missing values.
                // The config travels with the failure, so whoever asked knows which fields to show.
                return new(1, null, config, workspace, values,
                    string.Join("\n", errors.Select(e => e.Message)), Empty, loaded.Notes, loaded.Origin);
        }

        var context = new InterpolationContext(values, workspace, RepoName(repo), reference);

        RunPlan plan;
        try
        {
            plan = RunPlanBuilder.Build(config, context, OSKinds.Current, repo, reference, checkout.Commit);
        }
        catch (InterpolationException e)
        {
            return Failed(e.Message);
        }

        return new(0, plan, config, workspace, values, null, loaded.Others, loaded.Notes, loaded.Origin);
    }

    /// <summary>
    /// Leaves URLs git already understands alone - the tests, and anyone running a local mirror,
    /// depend on that. Only shorthand goes through the normaliser's whitelist.
    /// </summary>
    public static string Normalize(string input)
    {
        if (Uri.TryCreate(input, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https" or "file" or "ssh")
            return input;

        return GitClient.NormalizeRepoUrl(input);
    }

    public static string DefaultRef(GitClient git, string repo)
    {
        var (branches, _) = git.ListBranches(repo);
        if (branches is null || branches.Count == 0) return "HEAD";

        return new[] { "main", "master" }.FirstOrDefault(branches.Contains) ?? branches[0];
    }

    private static string ResolveRoot(string workspace, string? subdir)
    {
        if (string.IsNullOrWhiteSpace(subdir)) return workspace;

        var resolved = Path.GetFullPath(Path.Combine(workspace, subdir));
        var root = Path.GetFullPath(workspace);
        if (!resolved.StartsWith(root, StringComparison.Ordinal))
            throw new ArgumentException($"subdir '{subdir}' points outside the repository");

        return resolved;
    }

    /// <summary>
    /// Where the config comes from, most specific first: the text the caller handed over, a config
    /// named on the command line, your own override for this repository, the repository's own
    /// quickrun.yml, another launcher's scripts, and only then detection.
    /// </summary>
    private static (RunConfig? Config, string? Error, IReadOnlyList<Candidate> Others,
        IReadOnlyList<string> Notes, ConfigOrigin Origin) LoadConfig(
        string root, RunArgs args, string repo, ConfigOverrides overrides)
    {
        var explicitPath = args.ConfigPath;

        if (args.ConfigText is { } supplied)
        {
            try
            {
                return (ConfigParser.Parse(supplied, OSKinds.Current), null, Empty,
                    new[] { "using the config you supplied, not the one in the repository" },
                    ConfigOrigin.Supplied);
            }
            catch (ConfigException e)
            {
                return (null, $"the config you supplied: {e.Message}", Empty, NoNotes, ConfigOrigin.Supplied);
            }
        }

        var file = explicitPath is null
            ? null
            : Path.Combine(root, explicitPath);

        if (file is not null)
        {
            if (!File.Exists(file))
                return (null, $"config '{explicitPath}' does not exist in {repo}", Empty, NoNotes, ConfigOrigin.Explicit);

            try
            {
                return (ConfigParser.Parse(File.ReadAllText(file), OSKinds.Current), null, Empty,
                    new[] { $"using {explicitPath}, which you named" }, ConfigOrigin.Explicit);
            }
            catch (ConfigException e)
            {
                return (null, $"{Path.GetFileName(file)}: {e.Message}", Empty, NoNotes, ConfigOrigin.Explicit);
            }
        }

        // Your own config for this repository. It beats the repository's, which is the point - but
        // that has to be said out loud, or a run that ignores a committed quickrun.yml is a mystery.
        if (overrides.Read(repo) is { } mine)
        {
            var note = ConfigParser.FindConfigFile(root) is null
                ? "using your local config for this repository"
                : "using your local config for this repository, not the quickrun.yml it ships";

            try { return (ConfigParser.Parse(mine, OSKinds.Current), null, Empty, new[] { note }, ConfigOrigin.Local); }
            catch (ConfigException e)
            {
                return (null, $"your local config for {repo}: {e.Message}", Empty, NoNotes, ConfigOrigin.Local);
            }
        }

        if (ConfigParser.FindConfigFile(root) is { } own)
        {
            try
            {
                return (ConfigParser.Parse(File.ReadAllText(own), OSKinds.Current), null, Empty, NoNotes,
                    ConfigOrigin.Repository);
            }
            catch (ConfigException e)
            {
                return (null, $"{Path.GetFileName(own)}: {e.Message}", Empty, NoNotes, ConfigOrigin.Repository);
            }
        }

        // A repository written for another launcher says how to start itself, which beats anything
        // guessing from file names: Pinokio's own scripts come before the detector.
        if (Pinokio.Load(root, OSKinds.Current) is { } foreign)
            return (foreign.Config, null, Empty,
                foreign.Notes.Prepend($"no quickrun.yml - running this repository from its {foreign.Kind} scripts").ToList(),
                ConfigOrigin.Foreign);

        // The detector already ranks a root run script highest, so there is no separate lookup.
        var candidates = Detector.Detect(root, OSKinds.Current);
        if (candidates.Count > 0)
        {
            var yaml = Detector.ToYaml(candidates[0], RepoName(repo));
            return (ConfigParser.Parse(yaml, OSKinds.Current), null, candidates.Skip(1).ToList(),
                new[] { $"no quickrun.yml - detected {candidates[0].Label}" }, ConfigOrigin.Detected);
        }

        // A Pinokio repository whose scripts are JavaScript functions is a real case, and "nothing
        // detectable" would send the reader looking for a file that is right there.
        var pinokio = Pinokio.Present(root)
            ? " (its Pinokio scripts build their steps in JavaScript, which QuickRun cannot read)"
            : "";

        return (null,
            $"no quickrun.yml, no run script and nothing detectable in {repo}{pinokio} - see {ConfigDocs}",
            Empty, NoNotes, ConfigOrigin.Detected);
    }

    private static string Describe(ValidationIssue issue) =>
        string.IsNullOrEmpty(issue.Path) ? issue.Message : $"{issue.Path}: {issue.Message}";

    public static string RepoName(string repoUrl)
    {
        var last = repoUrl.TrimEnd('/').Split('/').LastOrDefault() ?? "repo";
        return last.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? last[..^4] : last;
    }

    private static IReadOnlyList<Candidate> Empty => Array.Empty<Candidate>();

    private static IReadOnlyList<string> NoNotes => Array.Empty<string>();

    private static RunPreparation Usage(string message) => new(2, null, null, null, null, message, Empty, NoNotes);

    private static RunPreparation Failed(string message) => new(1, null, null, null, null, message, Empty, NoNotes);
}
