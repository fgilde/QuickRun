using QuickRun.Core.Workspace;

namespace QuickRun.Core.Tests;

/// <summary>
/// Removing workspaces, and being told when it did not happen.
/// <para>
/// Every test here is a report. A machine had fifteen directories under runs/: fourteen with no
/// metadata and no files in them - the shells left behind when a removal deleted the contents and
/// then failed on the directory itself, with the error swallowed. They were invisible in the list,
/// because a directory without metadata was skipped, so there was no way to remove them from inside
/// QuickRun at all, and "Remove" on the one that was visible looked like a button that did nothing.
/// </para>
/// </summary>
public class WorkspaceRemovalTests
{
    private sealed class Home : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "quickrun-ws-" + Guid.NewGuid().ToString("n")[..8]);

        public Home() => Directory.CreateDirectory(Path);

        public string Runs => System.IO.Path.Combine(Path, "runs");

        /// <summary>A workspace directory with no metadata, as an interrupted checkout leaves one.</summary>
        public string Undescribed(string id, bool withFiles)
        {
            var dir = System.IO.Path.Combine(Runs, id);
            Directory.CreateDirectory(dir);
            if (withFiles) File.WriteAllText(System.IO.Path.Combine(dir, "README.md"), "checked out");
            return dir;
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void A_workspace_with_no_metadata_is_listed_so_that_it_can_be_removed()
    {
        using var home = new Home();
        var store = new WorkspaceStore(home.Path);
        home.Undescribed("orphan__repo__main-abc123", withFiles: true);

        var listed = Assert.Single(store.List());

        Assert.Equal("orphan__repo__main-abc123", listed.Id);
        Assert.Contains("unknown", listed.Repo);
        Assert.True(listed.Bytes > 0);

        // And it goes, which is the point of listing it.
        Assert.True(store.Remove(listed.Id));
        Assert.Empty(store.List());
    }

    /// <summary>
    /// The fourteen on that machine. Nothing in them, nothing describing them, and no reason to ask
    /// anybody what to do about them.
    /// </summary>
    [Fact]
    public void An_empty_directory_left_behind_by_a_failed_removal_is_swept_up()
    {
        using var home = new Home();
        var store = new WorkspaceStore(home.Path);

        var shell = home.Undescribed("leftover__repo__main-def456", withFiles: false);

        // The real shape of one: the files went, the directory tree stayed. Forty-six empty
        // directories and no files, in the case this is written from.
        Directory.CreateDirectory(Path.Combine(shell, "src", "deep", "deeper"));
        Directory.CreateDirectory(Path.Combine(shell, "tests"));

        Assert.Empty(store.List());
        Assert.False(Directory.Exists(shell), "the empty shell should have been removed");
    }

    [Fact]
    public void Removing_a_workspace_that_is_not_there_is_false_rather_than_an_error()
    {
        using var home = new Home();
        var store = new WorkspaceStore(home.Path);

        Assert.False(store.Remove("never__existed__main-000000"));
    }

    /// <summary>
    /// One that will not go must not stop the rest, and must be named. "Remove all" used to give up
    /// on the first failure, which from the outside looked like it had done nothing at all.
    /// </summary>
    [Fact]
    public void One_locked_workspace_does_not_stop_the_others_and_is_reported()
    {
        using var home = new Home();
        var store = new WorkspaceStore(home.Path);

        store.Touch("a__repo__main-111111", "https://github.com/a/repo", "main", null, true);
        store.Touch("b__repo__main-222222", "https://github.com/b/repo", "main", null, true);
        store.Touch("c__repo__main-333333", "https://github.com/c/repo", "main", null, true);

        var locked = Path.Combine(home.Runs, "b__repo__main-222222", "held-open.bin");
        File.WriteAllText(locked, "x");

        // An open handle with no sharing is what a virus scanner or an editor amounts to here.
        using (var hold = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var outcome = store.RemoveEach(store.List());

            Assert.Equal(2, outcome.Removed);
            var complaint = Assert.Single(outcome.Failed);
            Assert.Contains("b__repo__main-222222", complaint);

            // Said in words a person can act on, not an error code.
            Assert.Contains("open", complaint);
        }

        // And once the handle goes, so does the workspace.
        Assert.True(store.Remove("b__repo__main-222222"));
        Assert.Empty(store.List());
    }

    [Fact]
    public void A_removal_that_cannot_finish_throws_rather_than_claiming_success()
    {
        using var home = new Home();
        var store = new WorkspaceStore(home.Path);

        store.Touch("held__repo__main-444444", "https://github.com/held/repo", "main", null, null);
        var locked = Path.Combine(home.Runs, "held__repo__main-444444", "held-open.bin");
        File.WriteAllText(locked, "x");

        using var hold = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);

        // Windows refuses; POSIX unlinks an open file quite happily, so there it simply succeeds.
        if (OperatingSystem.IsWindows())
            Assert.Throws<IOException>(() => store.Remove("held__repo__main-444444"));
        else
            Assert.True(store.Remove("held__repo__main-444444"));
    }
}
