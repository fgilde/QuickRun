using QuickRun.Core;
using QuickRun.Core.Config;
using QuickRun.Core.Detect;
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
    string? ConfigPath);

public sealed record RunPreparation(
    int ExitCode,
    RunPlan? Plan,
    RunConfig? Config,
    string? Workspace,
    IReadOnlyDictionary<string, string?>? Values,
    string? Error,
    IReadOnlyList<Candidate> OtherCandidates);

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

        var loaded = LoadConfig(root, args.ConfigPath, repo);
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
                return Failed(string.Join("\n", errors.Select(e => e.Message)));
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

        return new(0, plan, config, workspace, values, null, loaded.Others);
    }

    /// <summary>
    /// Leaves URLs git already understands alone - the tests, and anyone running a local mirror,
    /// depend on that. Only shorthand goes through the normaliser's whitelist.
    /// </summary>
    private static string Normalize(string input)
    {
        if (Uri.TryCreate(input, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https" or "file" or "ssh")
            return input;

        return GitClient.NormalizeRepoUrl(input);
    }

    private static string DefaultRef(GitClient git, string repo)
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

    private static (RunConfig? Config, string? Error, IReadOnlyList<Candidate> Others) LoadConfig(
        string root, string? explicitPath, string repo)
    {
        var file = explicitPath is null
            ? ConfigParser.FindConfigFile(root)
            : Path.Combine(root, explicitPath);

        if (file is not null)
        {
            if (!File.Exists(file)) return (null, $"config '{explicitPath}' does not exist in {repo}", Empty);
            try { return (ConfigParser.Parse(File.ReadAllText(file), OSKinds.Current), null, Empty); }
            catch (ConfigException e) { return (null, $"{Path.GetFileName(file)}: {e.Message}", Empty); }
        }

        // The detector already ranks a root run script highest, so there is no separate lookup.
        var candidates = Detector.Detect(root, OSKinds.Current);
        if (candidates.Count > 0)
        {
            var yaml = Detector.ToYaml(candidates[0], RepoName(repo));
            return (ConfigParser.Parse(yaml, OSKinds.Current), null, candidates.Skip(1).ToList());
        }

        return (null,
            $"no quickrun.yml, no run script and nothing detectable in {repo} - see {ConfigDocs}",
            Empty);
    }

    private static string Describe(ValidationIssue issue) =>
        string.IsNullOrEmpty(issue.Path) ? issue.Message : $"{issue.Path}: {issue.Message}";

    public static string RepoName(string repoUrl)
    {
        var last = repoUrl.TrimEnd('/').Split('/').LastOrDefault() ?? "repo";
        return last.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? last[..^4] : last;
    }

    private static IReadOnlyList<Candidate> Empty => Array.Empty<Candidate>();

    private static RunPreparation Usage(string message) => new(2, null, null, null, null, message, Empty);

    private static RunPreparation Failed(string message) => new(1, null, null, null, null, message, Empty);
}
