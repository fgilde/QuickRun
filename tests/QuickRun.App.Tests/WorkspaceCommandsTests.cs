using QuickRun.App.Commands;
using QuickRun.Core.Tests;
using QuickRun.Core.Workspace;

namespace QuickRun.App.Tests;

public class WorkspaceCommandsTests
{
    private static WorkspaceStore Seed(TempHome home, params string[] names)
    {
        var store = new WorkspaceStore(home.Path);
        foreach (var name in names)
        {
            var url = $"https://github.com/acme/{name}";
            Directory.CreateDirectory(store.PathFor(url, "main"));
            store.Touch(WorkspaceStore.IdFor(url, "main"), url, "main", null, null);
        }
        return store;
    }

    [Theory]
    [InlineData("30d", 30 * 24)]
    [InlineData("12h", 12)]
    [InlineData("2w", 14 * 24)]
    public void ParseAge_understands_days_hours_and_weeks(string text, double expectedHours)
        => Assert.Equal(expectedHours, WorkspaceOps.ParseAge(text)!.Value.TotalHours, 3);

    [Fact]
    public void ParseAge_returns_null_for_null()
        => Assert.Null(WorkspaceOps.ParseAge(null));

    [Theory]
    [InlineData("30")]
    [InlineData("30x")]
    [InlineData("d30")]
    [InlineData("")]
    public void ParseAge_rejects_anything_else(string text)
        => Assert.Throws<ArgumentException>(() => WorkspaceOps.ParseAge(text));

    [Fact]
    public void Clean_without_any_selector_is_a_usage_error()
    {
        using var home = new TempHome();
        var result = WorkspaceOps.Clean(Seed(home, "a"), new CleanRequest(false, null, null));

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(0, result.Removed);
    }

    [Fact]
    public void Clean_with_more_than_one_selector_is_a_usage_error()
    {
        using var home = new TempHome();
        Assert.Equal(2, WorkspaceOps.Clean(Seed(home, "a"),
            new CleanRequest(true, TimeSpan.FromDays(1), null)).ExitCode);
    }

    [Fact]
    public void Clean_all_removes_every_workspace()
    {
        using var home = new TempHome();
        var store = Seed(home, "a", "b", "c");

        var result = WorkspaceOps.Clean(store, new CleanRequest(true, null, null));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(3, result.Removed);
        Assert.Empty(store.List());
    }

    [Fact]
    public void Clean_by_id_removes_only_that_workspace()
    {
        using var home = new TempHome();
        var store = Seed(home, "a", "b");
        var id = store.List().First().Id;

        var result = WorkspaceOps.Clean(store, new CleanRequest(false, null, id));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, result.Removed);
        Assert.Single(store.List());
    }

    [Fact]
    public void Clean_by_an_unknown_id_reports_an_error()
    {
        using var home = new TempHome();
        var result = WorkspaceOps.Clean(Seed(home, "a"), new CleanRequest(false, null, "acme__nothing__main-000000"));

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(0, result.Removed);
    }

    [Fact]
    public void Clean_by_age_keeps_recent_workspaces()
    {
        using var home = new TempHome();
        var store = Seed(home, "a", "b");

        var result = WorkspaceOps.Clean(store, new CleanRequest(false, TimeSpan.FromDays(30), null));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, result.Removed);
        Assert.Equal(2, store.List().Count);
    }

    [Fact]
    public void Clean_rejects_an_id_that_tries_to_escape_the_root()
    {
        using var home = new TempHome();
        var result = WorkspaceOps.Clean(Seed(home, "a"), new CleanRequest(false, null, "../../windows"));

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(0, result.Removed);
    }
}
