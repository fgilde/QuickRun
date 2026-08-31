using QuickRun.Core;
using QuickRun.Core.Config;
using QuickRun.Core.Detect;
using QuickRun.Core.Foreign;
using QuickRun.Core.Git;
using QuickRun.Core.Inputs;
using QuickRun.Core.Process;
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
    string? ConfigText = null,
    /// <summary>
    /// A folder on this machine to run, instead of a repository to check out.
    /// <para>
    /// Deliberately a field of its own rather than a path smuggled into <see cref="Repo"/>: running
    /// a local folder is only ever asked for from the command line or a shell verb, never by a web
    /// page through the extension, and a separate field is what keeps those apart.
    /// </para>
    /// </summary>
    string? LocalPath = null,
    /// <summary>
    /// Run a copy of that folder under runs/ instead of the folder itself. Slower and it starts
    /// from a clean slate, but nothing the run does can reach the original.
    /// </summary>
    bool Copy = false);

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
    ConfigOrigin Origin = ConfigOrigin.Repository,
    /// <summary>
    /// The folder being run where it lies, when that is what this is. Null for a checkout and for a
    /// copy, because both of those live under runs/ and QuickRun owns them. Whoever records the
    /// workspace after the run has to pass this back, or the note stops pointing anywhere.
    /// </summary>
    string? LocalFolder = null,
    /// <summary>
    /// Which workspace this run belongs to.
    /// <para>
    /// Carried rather than worked out again afterwards: the id depends on more than the repository
    /// and the ref - a copied folder has a variant of its own - and recomputing it from the plan got
    /// that wrong, so recording the outcome wrote over a different workspace's note.
    /// </para>
    /// </summary>
    string? WorkspaceId = null);

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
    private const string ConfigDocs = "https://quickrun.org/docs/config";

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
        IReadOnlyDictionary<string, string?> provided;
        try { provided = InputResolver.ParseAssignments(args.Inputs); }
        catch (ArgumentException e) { return Usage(e.Message); }

        string repo;
        string reference;
        string workspace;
        string? commit;
        string workspaceId;
        var notes = new List<string>();

        if (args.LocalPath is not null)
        {
            var local = Local(args, store, notes);
            if (local.Error is { } localError) return Usage(localError);

            (repo, reference, workspace, commit) = (local.Repo!, local.Ref!, local.Workspace!, local.Commit);
            workspaceId = WorkspaceStore.IdFor(repo, reference, args.Copy ? CopyVariant : null);
        }
        else
        {
            try { repo = Normalize(args.Repo); }
            catch (ArgumentException e) { return Usage(e.Message); }

            reference = args.Ref ?? DefaultRef(git, repo);
            workspace = store.PathFor(repo, reference);

            var checkout = git.CheckoutOrUpdate(repo, reference, args.PullRequest, workspace, args.Fresh);
            if (!checkout.Ok) return Failed(checkout.Error ?? "checkout failed");

            commit = checkout.Commit;
            workspaceId = WorkspaceStore.IdFor(repo, reference);
            store.Touch(workspaceId, repo, reference, commit, null);
        }

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
            plan = RunPlanBuilder.Build(config, context, OSKinds.Current, repo, reference, commit);
        }
        catch (InterpolationException e)
        {
            return Failed(e.Message);
        }

        // The local branch has things to say - that the folder is run where it lies, what a copy
        // left out - and they belong with the config's own notes, above the command list.
        return new(0, plan, config, workspace, values, null, loaded.Others,
            notes.Count == 0 ? loaded.Notes : notes.Concat(loaded.Notes).ToList(), loaded.Origin,
            LocalFolder: args.LocalPath is not null && !args.Copy ? workspace : null,
            WorkspaceId: workspaceId);
    }

    /// <summary>Marks the workspace of a copied folder, so it is not the one for running in place.</summary>
    private const string CopyVariant = "copy";

    /// <summary>Directories a copy leaves behind, because the setup steps put them back.</summary>
    private static readonly string[] Regenerated =
    [
        ".git", "node_modules", ".venv", "venv", "__pycache__", "obj", "bin", "target", ".next",
        ".nuxt", ".gradle", ".pytest_cache", ".mypy_cache",
    ];

    /// <summary>
    /// Prepares a folder on this machine to be run.
    /// <para>
    /// In place by default. Copying it first sounds safer and is mostly worse: a checkout of
    /// somebody's working copy is a different project - its own database file, its own .env, its
    /// own absolute paths - and the build lands somewhere they will never look. What QuickRun runs
    /// is the folder they pointed at, and the note it keeps under runs/ is all a removal can reach.
    /// </para>
    /// </summary>
    private static (string? Repo, string? Ref, string? Workspace, string? Commit, string? Error) Local(
        RunArgs args, WorkspaceStore store, List<string> notes)
    {
        string folder;
        try { folder = Path.GetFullPath(args.LocalPath!); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return (null, null, null, null, $"'{args.LocalPath}' is not a usable path: {e.Message}");
        }

        if (!Directory.Exists(folder)) return (null, null, null, null, $"'{folder}' is not a folder");

        // Running QuickRun's own workspace root would copy a copy into itself, and a run of a
        // workspace is a run of the repository it came from - which has its own way in.
        var root = Path.GetFullPath(store.Root);
        if (folder.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return (null, null, null, null,
                $"'{folder}' is inside QuickRun's own directory - run the repository it came from instead");

        // The path is the identity: it keys the workspace note, any config saved for it, and the
        // trust record, exactly as a repository URL does for a checkout.
        var repo = folder;
        var (reference, commit) = GitState(folder, args.Ref);

        if (!args.Copy)
        {
            store.Touch(WorkspaceStore.IdFor(repo, reference), repo, reference, commit, null, folder);
            notes.Add($"running {folder} where it is - nothing was checked out and nothing was copied");
            return (repo, reference, folder, commit, null);
        }


        // A variant of its own: the same folder run in place and run as a copy are two workspaces,
        // and the note about one must not overwrite the other.
        var workspace = store.PathFor(repo, reference, CopyVariant);

        try
        {
            CopyInto(folder, workspace, args.Fresh);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (null, null, null, null, $"could not copy {folder}: {e.Message}");
        }

        store.Touch(WorkspaceStore.IdFor(repo, reference, CopyVariant), repo, reference, commit, null);
        notes.Add($"running a copy of {folder} under {workspace}, so the original is untouched");
        notes.Add($"left out of the copy, because the setup steps put them back: {string.Join(", ", Regenerated)}");

        return (repo, reference, workspace, commit, null);
    }

    /// <summary>
    /// What git says about a folder, when it is a working copy at all.
    /// <para>
    /// Worth asking: a branch name and a commit are what make a local run identifiable afterwards,
    /// and they cost one process each. A folder that is not a repository is simply "local".
    /// </para>
    /// </summary>
    private static (string Ref, string? Commit) GitState(string folder, string? requested)
    {
        if (requested is not null) return (requested, Ask(folder, "rev-parse", "HEAD"));
        if (!Directory.Exists(Path.Combine(folder, ".git"))) return ("local", null);

        var branch = Ask(folder, "rev-parse", "--abbrev-ref", "HEAD");
        var commit = Ask(folder, "rev-parse", "HEAD");

        // A detached head has no branch name to report, and "HEAD" is not one.
        return (string.IsNullOrEmpty(branch) || branch == "HEAD" ? "local" : branch, commit);
    }

    private static string? Ask(string folder, params string[] arguments)
    {
        var result = CommandRunner.Capture("git", arguments, folder, timeoutMs: 5_000);
        return result.ExitCode == 0 && result.Output.Length > 0 ? result.Output.Trim() : null;
    }

    /// <summary>Copies a folder, leaving out what a build regenerates anyway.</summary>
    private static void CopyInto(string from, string to, bool fresh)
    {
        if (fresh && Directory.Exists(to)) Directory.Delete(to, recursive: true);
        Directory.CreateDirectory(to);

        var skip = new HashSet<string>(Regenerated, StringComparer.OrdinalIgnoreCase);

        foreach (var source in Directory.EnumerateFileSystemEntries(from))
        {
            var name = Path.GetFileName(source);
            if (skip.Contains(name)) continue;

            var target = Path.Combine(to, name);

            if (Directory.Exists(source)) CopyTree(source, target, skip);
            else File.Copy(source, target, overwrite: true);
        }
    }

    private static void CopyTree(string from, string to, HashSet<string> skip)
    {
        Directory.CreateDirectory(to);

        foreach (var source in Directory.EnumerateFileSystemEntries(from))
        {
            var name = Path.GetFileName(source);
            if (skip.Contains(name)) continue;

            var target = Path.Combine(to, name);

            if (Directory.Exists(source)) CopyTree(source, target, skip);
            else File.Copy(source, target, overwrite: true);
        }
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
            // A relative name means a file in the checkout, and "../.." must not turn it into one
            // outside. An absolute path stays allowed: that is somebody at a command line naming a
            // config of their own, which is a different thing from a name arriving over HTTP.
            if (!Path.IsPathRooted(explicitPath)
                && !Path.GetFullPath(file).StartsWith(Path.GetFullPath(root), StringComparison.Ordinal))
                return (null, $"config '{explicitPath}' is outside {repo}", Empty, NoNotes, ConfigOrigin.Explicit);

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
