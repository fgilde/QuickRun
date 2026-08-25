using QuickRun.App.Daemon;
using QuickRun.Core.Workspace;

namespace QuickRun.App.Tests;

/// <summary>
/// What the local UI offers in its branch picker. The point is that the useful ref is preselected:
/// a repository with two hundred feature branches must not open on the first one alphabetically.
/// </summary>
public class RefSuggestionsTests
{
    private static WorkspaceInfo Workspace(string repo, string reference, int daysAgo) =>
        new($"{repo}-{reference}", $"C:/tmp/{reference}", repo, reference, 0,
            DateTimeOffset.UtcNow.AddDays(-daysAgo), null, null);

    [Fact]
    public void The_refs_of_this_repository_come_back_newest_first()
    {
        var workspaces = new[]
        {
            Workspace("https://github.com/acme/app", "main", 3),
            Workspace("https://github.com/acme/app", "release/2.0", 1),
            Workspace("https://github.com/other/thing", "main", 0),
        };

        Assert.Equal(new[] { "release/2.0", "main" },
            RefSuggestions.Recent(workspaces, "https://github.com/acme/app"));
    }

    [Fact]
    public void A_git_suffix_or_trailing_slash_is_the_same_repository()
    {
        var workspaces = new[] { Workspace("https://github.com/acme/app.git", "main", 0) };

        Assert.Equal(new[] { "main" }, RefSuggestions.Recent(workspaces, "https://github.com/acme/app"));
        Assert.Equal(new[] { "main" }, RefSuggestions.Recent(workspaces, "https://github.com/acme/app/"));
    }

    [Fact]
    public void Only_five_are_offered()
    {
        var workspaces = Enumerable.Range(0, 9)
            .Select(i => Workspace("https://github.com/acme/app", $"branch-{i}", i))
            .ToList();

        Assert.Equal(5, RefSuggestions.Recent(workspaces, "https://github.com/acme/app").Count);
    }

    [Fact]
    public void Main_wins_when_nothing_was_run_before() =>
        Assert.Equal("main", RefSuggestions.Default(new[] { "develop", "main", "master" }, Array.Empty<string>()));

    [Fact]
    public void Master_is_taken_when_there_is_no_main() =>
        Assert.Equal("master", RefSuggestions.Default(new[] { "master", "topic" }, Array.Empty<string>()));

    [Fact]
    public void The_first_branch_is_better_than_nothing() =>
        Assert.Equal("topic", RefSuggestions.Default(new[] { "topic", "other" }, Array.Empty<string>()));

    /// <summary>What this user came back for beats what the convention says.</summary>
    [Fact]
    public void A_ref_run_before_is_preselected() =>
        Assert.Equal("release/2.0",
            RefSuggestions.Default(new[] { "main", "release/2.0" }, new[] { "release/2.0", "main" }));

    [Fact]
    public void A_recent_ref_that_no_longer_exists_is_not_preselected() =>
        Assert.Equal("main", RefSuggestions.Default(new[] { "main" }, new[] { "deleted-branch" }));

    /// <summary>
    /// A repository whose branches could not be listed - private, offline, a typo - is still
    /// runnable, and the last ref used is the best offer left.
    /// </summary>
    [Fact]
    public void Without_a_branch_list_the_last_used_ref_is_the_offer() =>
        Assert.Equal("main", RefSuggestions.Default(Array.Empty<string>(), new[] { "main" }));

    [Fact]
    public void Without_anything_at_all_there_is_no_preselection() =>
        Assert.Null(RefSuggestions.Default(Array.Empty<string>(), Array.Empty<string>()));
}
