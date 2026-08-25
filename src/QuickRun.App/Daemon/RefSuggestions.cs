using QuickRun.Core.Workspace;

namespace QuickRun.App.Daemon;

/// <summary>
/// What to offer in the local UI's branch picker.
/// <para>
/// Listing a repository's branches is a network call, and it says nothing about which of them this
/// user actually runs. The refs already on disk do: they are the ones worth putting at the top,
/// ahead of a hundred alphabetical feature branches.
/// </para>
/// </summary>
public static class RefSuggestions
{
    private const int MaxRecent = 5;

    /// <summary>The refs of this repository that have been run before, newest first.</summary>
    public static IReadOnlyList<string> Recent(IEnumerable<WorkspaceInfo> workspaces, string repo) =>
        workspaces
            .Where(w => SameRepo(w.Repo, repo))
            .OrderByDescending(w => w.LastUsed)
            .Select(w => w.Ref)
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Distinct(StringComparer.Ordinal)
            .Take(MaxRecent)
            .ToList();

    /// <summary>
    /// Which branch to preselect. Same rule the CLI uses when no ref is given, so the UI cannot
    /// disagree with what <c>quickrun run</c> would have done.
    /// </summary>
    public static string? Default(IReadOnlyList<string> branches, IReadOnlyList<string> recent)
    {
        if (branches.Count == 0) return recent.FirstOrDefault();

        // A ref this user ran before beats convention: it is what they came back for.
        if (recent.FirstOrDefault(branches.Contains) is { } known) return known;

        return new[] { "main", "master" }.FirstOrDefault(branches.Contains) ?? branches[0];
    }

    /// <summary>
    /// Whether two repository URLs mean the same thing. A workspace records what it was checked out
    /// from, which may carry a .git suffix or a different case of host that the user did not type.
    /// </summary>
    private static bool SameRepo(string stored, string asked) =>
        string.Equals(Trim(stored), Trim(asked), StringComparison.OrdinalIgnoreCase);

    private static string Trim(string repo)
    {
        var value = repo.Trim().TrimEnd('/');
        return value.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? value[..^4] : value;
    }
}
