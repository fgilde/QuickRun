using QuickRun.App.Commands;
using QuickRun.App.Daemon;
using QuickRun.Core.Tests;
using QuickRun.Core.Workspace;

namespace QuickRun.App.Tests;

/// <summary>
/// Removing checkouts nobody has used for a while.
/// <para>
/// The interesting half is what it refuses to take. A workspace with a plan waiting in it, or a run
/// going in it, has an old date the moment the setting is short enough - and removing it would pull
/// the ground out from under something somebody is looking at. It cannot be left to the file system
/// to object either: a compose run holds no handle on the checkout at all, so the directory deletes
/// cleanly while the application carries on.
/// </para>
/// </summary>
public class WorkspaceCleanupTests
{
    private static RunArgs Args(string repo) =>
        new(repo, "main", null, null, Array.Empty<string>(), null, false, true, true, null);

    /// <summary>A workspace that was recorded long ago and belongs to nothing.</summary>
    private static string Abandoned(WorkspaceStore store, string repo, DateTimeOffset when)
    {
        var id = WorkspaceStore.IdFor(repo, "main");
        var dir = store.PathFor(repo, "main");

        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "README.md"), "old");
        store.Touch(id, repo, "main", "abc1234", true);

        // The date is what decides, and there is no waiting a month in a test: the metadata file is
        // where the store reads it from.
        var meta = Path.Combine(dir, ".quickrun-meta.json");
        var text = File.ReadAllText(meta);
        File.WriteAllText(meta, text.Replace(
            $"\"lastUsed\":\"{DateTimeOffset.UtcNow:yyyy-MM-dd}",
            $"\"lastUsed\":\"{when:yyyy-MM-dd}"));

        return dir;
    }

    [Fact]
    public void An_old_checkout_goes()
    {
        using var home = new TempHome();
        var store = new WorkspaceStore(home.Path);
        var registry = new RunRegistry(store);

        var dir = Abandoned(store, "https://github.com/acme/old", DateTimeOffset.UtcNow.AddDays(-40));
        Assert.True(Directory.Exists(dir));

        var (removed, failed) = registry.CleanWorkspaces(TimeSpan.FromDays(30));

        Assert.Equal(1, removed);
        Assert.Empty(failed);
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void A_recent_one_stays()
    {
        using var home = new TempHome();
        var store = new WorkspaceStore(home.Path);
        var registry = new RunRegistry(store);

        var dir = Abandoned(store, "https://github.com/acme/recent", DateTimeOffset.UtcNow.AddDays(-3));

        Assert.Equal(0, registry.CleanWorkspaces(TimeSpan.FromDays(30)).Removed);
        Assert.True(Directory.Exists(dir));
    }

    /// <summary>
    /// The one that matters: a plan waiting for an answer is a workspace in use, whatever its date
    /// says. Its files are checked out and nothing holds them open, so only knowing about the run
    /// keeps it there.
    /// </summary>
    [Fact]
    public async Task A_workspace_with_a_plan_waiting_in_it_is_left_alone()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();

        repo.Write("quickrun.yml", "run: echo hi\n");
        repo.Commit("add config");

        var store = new WorkspaceStore(home.Path);
        var registry = new RunRegistry(store);

        var (summary, error) = await registry.PrepareAsync(Args(repo.Url));

        Assert.Null(error);
        Assert.NotNull(summary!.Workspace);
        Assert.True(Directory.Exists(summary.Workspace));

        // Everything is older than nothing: without the check for what is in use, this would take
        // the checkout out from under the plan that is waiting to be approved.
        var (removed, _) = registry.CleanWorkspaces(TimeSpan.Zero);

        Assert.Equal(0, removed);
        Assert.True(Directory.Exists(summary.Workspace));
    }
}
