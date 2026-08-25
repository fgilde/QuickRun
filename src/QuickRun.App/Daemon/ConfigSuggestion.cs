using QuickRun.App.Commands;
using QuickRun.Core;
using QuickRun.Core.Config;
using QuickRun.Core.Detect;
using QuickRun.Core.Foreign;
using QuickRun.Core.Git;
using QuickRun.Core.Workspace;

namespace QuickRun.App.Daemon;

/// <param name="Source">
/// Where the text came from: <c>yours</c>, <c>repo</c>, <c>pinokio</c>, <c>detected</c> or
/// <c>template</c>. The builder says which, because editing a repository's own config and editing a
/// guess are not the same thing.
/// </param>
public sealed record ConfigSuggestionResult(
    string Repo,
    string Ref,
    string Source,
    string Text,
    string? Note,
    string? Error);

/// <summary>
/// What to put in the editor when someone starts writing a config for a repository.
/// <para>
/// An empty editor is the worst starting point: almost every repository already says something about
/// how it starts - its own config, another launcher's scripts, or a recognisable entry point - and
/// the fastest way to a working config is to edit that rather than to type it from nothing.
/// </para>
/// </summary>
public static class ConfigSuggestion
{
    public static ConfigSuggestionResult For(string repoInput, string? reference, string? token, WorkspaceStore store)
    {
        string repo;
        try { repo = RunPipeline.Normalize(repoInput); }
        catch (ArgumentException e) { return Failed(repoInput, reference, e.Message); }

        var overrides = new ConfigOverrides(store.Root);
        if (overrides.Read(repo) is { } mine)
            return new(repo, reference ?? "", "yours", mine,
                "this is the config you saved for this repository", null);

        var git = new GitClient(new CredentialResolver(token));
        var chosen = reference ?? RunPipeline.DefaultRef(git, repo);
        var workspace = store.PathFor(repo, chosen);

        var checkout = git.CheckoutOrUpdate(repo, chosen, null, workspace, fresh: false);
        if (!checkout.Ok) return Failed(repo, chosen, checkout.Error ?? "the repository could not be checked out");

        store.Touch(WorkspaceStore.IdFor(repo, chosen), repo, chosen, checkout.Commit, null);

        return From(repo, chosen, workspace);
    }

    private static ConfigSuggestionResult From(string repo, string reference, string workspace)
    {
        if (ConfigParser.FindConfigFile(workspace) is { } own)
        {
            try
            {
                return new(repo, reference, "repo", File.ReadAllText(own),
                    $"this is the {Path.GetFileName(own)} the repository ships - your edits stay yours until you save them",
                    null);
            }
            catch (IOException e)
            {
                return Failed(repo, reference, e.Message);
            }
        }

        if (Pinokio.Load(workspace, OSKinds.Current) is { } foreign)
            return new(repo, reference, foreign.Kind,
                ConfigWriter.ToYaml(foreign.Config,
                    $"generated from this repository's {foreign.Kind} scripts - review it"),
                Note(foreign),
                null);

        var candidates = Detector.Detect(workspace, OSKinds.Current);
        if (candidates.Count > 0)
            return new(repo, reference, "detected",
                Detector.ToYaml(candidates[0], RunPipeline.RepoName(repo)),
                $"nothing was committed here, so this is QuickRun's guess: {candidates[0].Label}",
                null);

        return new(repo, reference, "template", Template(RunPipeline.RepoName(repo)),
            "nothing recognisable in this repository - this is an empty starting point", null);
    }

    private static string? Note(ForeignConfig foreign) =>
        foreign.Notes.Count == 0
            ? $"generated from this repository's {foreign.Kind} scripts"
            : $"generated from this repository's {foreign.Kind} scripts. {string.Join(" ", foreign.Notes)}";

    private static string Template(string name) =>
        $$"""
         {{ConfigWriter.SchemaLine}}
         version: 1
         name: {{name}}

         requires:
           - tool: node
             version: ">=20.0"
             install: https://nodejs.org

         setup:
           - run: npm ci

         tasks:
           - name: dev
             run: npm run dev
             readyWhen: {http: "http://localhost:5173"}
             open: true

         """;

    private static ConfigSuggestionResult Failed(string repo, string? reference, string error) =>
        new(repo, reference ?? "", "none", "", null, error);
}
