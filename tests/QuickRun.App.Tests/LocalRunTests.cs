using QuickRun.App.Commands;
using QuickRun.Core.Git;
using QuickRun.Core.Tests;
using QuickRun.Core.Workspace;

namespace QuickRun.App.Tests;

/// <summary>
/// Running a folder that is already on this machine, with no repository to check out.
/// <para>
/// The dangerous part is not the running - it is that QuickRun now knows about a directory it does
/// not own. What it keeps under runs/ is a note saying where that directory is, and a removal can
/// take away the note and nothing else. Every test about deleting here exists to keep it that way.
/// </para>
/// </summary>
public class LocalRunTests
{
    private sealed class Folder : IDisposable
    {
        public string Path { get; }

        public Folder(string config = "tasks:\n  - name: hello\n    run: echo hi\n")
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "quickrun-folder-" + Guid.NewGuid().ToString("n")[..8]);

            Directory.CreateDirectory(Path);
            File.WriteAllText(System.IO.Path.Combine(Path, "quickrun.yml"), config);

            // Something to check is still there afterwards, which is the whole point.
            File.WriteAllText(System.IO.Path.Combine(Path, "mine.txt"), "written by hand");
        }

        /// <summary>A directory a build would recreate, so a copy is expected to leave it out.</summary>
        public void WithRegenerated()
        {
            Directory.CreateDirectory(System.IO.Path.Combine(Path, "node_modules", "left-pad"));
            File.WriteAllText(System.IO.Path.Combine(Path, "node_modules", "left-pad", "index.js"), "//");
        }

        public bool Intact =>
            File.Exists(System.IO.Path.Combine(Path, "mine.txt"))
            && File.Exists(System.IO.Path.Combine(Path, "quickrun.yml"));

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
        }
    }

    private static RunArgs Args(string folder, bool copy = false) =>
        new("", null, null, null, Array.Empty<string>(), null, false, true, true, null,
            LocalPath: folder, Copy: copy);

    private static RunPreparation Prepare(WorkspaceStore store, RunArgs args) =>
        RunPipeline.Prepare(args, store, new GitClient(new CredentialResolver(null)),
            (_, provided) => provided);

    [Fact]
    public void A_folder_is_planned_where_it_lies()
    {
        using var folder = new Folder();
        using var home = new TempHome();
        var store = new WorkspaceStore(home.Path);

        var preparation = Prepare(store, Args(folder.Path));

        Assert.Equal(0, preparation.ExitCode);
        Assert.Equal(folder.Path, preparation.Workspace);
        Assert.Equal(folder.Path, preparation.Plan!.Repo);
        Assert.Equal("local", preparation.Plan.Ref);
        Assert.Null(preparation.Plan.Commit);

        // Said before anything runs, because "where does this actually run" is the one thing that
        // differs from every other kind of run.
        Assert.Contains(preparation.Notes, note => note.Contains("where it is"));

        // The workspace is a note, not a copy: the folder's own files are not under runs/.
        var listed = Assert.Single(store.List());
        Assert.True(listed.Local);
        Assert.Equal(folder.Path, listed.Path);
        Assert.Equal(0, listed.Bytes);
    }

    /// <summary>
    /// The one that must never regress. A local workspace is a note; removing it removes the note.
    /// </summary>
    [Fact]
    public void Removing_a_local_workspace_leaves_the_folder_alone()
    {
        using var folder = new Folder();
        using var home = new TempHome();
        var store = new WorkspaceStore(home.Path);

        Prepare(store, Args(folder.Path));
        var listed = Assert.Single(store.List());

        Assert.True(store.Remove(listed.Id));
        Assert.Empty(store.List());
        Assert.True(folder.Intact, "the folder QuickRun was pointed at must still be there");
        Assert.True(Directory.Exists(folder.Path));
    }

    [Fact]
    public void Removing_everything_leaves_the_folder_alone()
    {
        using var folder = new Folder();
        using var home = new TempHome();
        var store = new WorkspaceStore(home.Path);

        Prepare(store, Args(folder.Path));

        Assert.Equal(1, store.RemoveAll());
        Assert.True(folder.Intact, "Remove all must not reach outside runs/");
        Assert.True(Directory.Exists(folder.Path));
    }

    [Fact]
    public void Cleaning_by_age_leaves_the_folder_alone()
    {
        using var folder = new Folder();
        using var home = new TempHome();
        var store = new WorkspaceStore(home.Path);

        Prepare(store, Args(folder.Path));

        Assert.Equal(1, store.Clean(TimeSpan.Zero));
        Assert.True(folder.Intact);
        Assert.True(Directory.Exists(folder.Path));
    }

    [Fact]
    public void A_copy_runs_under_runs_and_leaves_out_what_a_build_regenerates()
    {
        using var folder = new Folder();
        using var home = new TempHome();
        folder.WithRegenerated();

        var store = new WorkspaceStore(home.Path);
        var preparation = Prepare(store, Args(folder.Path, copy: true));

        Assert.Equal(0, preparation.ExitCode);
        Assert.NotEqual(folder.Path, preparation.Workspace);
        Assert.StartsWith(Path.GetFullPath(home.Path), Path.GetFullPath(preparation.Workspace!));

        // The copy has the project and not the build output.
        Assert.True(File.Exists(Path.Combine(preparation.Workspace!, "mine.txt")));
        Assert.True(File.Exists(Path.Combine(preparation.Workspace!, "quickrun.yml")));
        Assert.False(Directory.Exists(Path.Combine(preparation.Workspace!, "node_modules")));

        // And it is a workspace QuickRun owns, so removing it is removing the copy.
        var listed = Assert.Single(store.List());
        Assert.False(listed.Local);
        Assert.True(store.Remove(listed.Id));
        Assert.True(folder.Intact, "the original must be untouched by a copy being removed");
    }

    /// <summary>
    /// The same folder run both ways is two workspaces. They used to share an id, so the second run
    /// overwrote the first one's note - and a note that had pointed at the folder started claiming a
    /// copy instead.
    /// </summary>
    [Fact]
    public void Running_in_place_and_running_a_copy_are_separate_workspaces()
    {
        using var folder = new Folder();
        using var home = new TempHome();
        var store = new WorkspaceStore(home.Path);

        Prepare(store, Args(folder.Path));
        Prepare(store, Args(folder.Path, copy: true));

        var listed = store.List();
        Assert.Equal(2, listed.Count);
        Assert.Single(listed, w => w.Local);
        Assert.Single(listed, w => !w.Local);
    }

    [Fact]
    public void A_folder_inside_QuickRuns_own_directory_is_refused()
    {
        using var home = new TempHome();
        var store = new WorkspaceStore(home.Path);

        var inside = Path.Combine(home.Path, "runs", "something");
        Directory.CreateDirectory(inside);
        File.WriteAllText(Path.Combine(inside, "quickrun.yml"), "tasks: []\n");

        var preparation = Prepare(store, Args(inside));

        Assert.NotEqual(0, preparation.ExitCode);
        Assert.Contains("inside QuickRun's own directory", preparation.Error);
    }

    [Fact]
    public void A_folder_that_is_not_there_is_a_usage_error()
    {
        using var home = new TempHome();
        var store = new WorkspaceStore(home.Path);

        var preparation = Prepare(store, Args(Path.Combine(Path.GetTempPath(), "quickrun-nope-" + Guid.NewGuid())));

        Assert.Equal(2, preparation.ExitCode);
        Assert.Contains("is not a folder", preparation.Error);
    }

    /// <summary>
    /// A working copy has a branch and a commit, and saying so is what makes a local run
    /// identifiable in the list afterwards.
    /// </summary>
    [Fact]
    public void A_folder_that_is_a_git_working_copy_reports_its_branch_and_commit()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();

        repo.Write("quickrun.yml", "tasks:\n  - name: hello\n    run: echo hi\n");
        repo.Commit("add config");

        var preparation = Prepare(new WorkspaceStore(home.Path), Args(repo.Path));

        Assert.Equal(0, preparation.ExitCode);
        Assert.Equal("main", preparation.Plan!.Ref);
        Assert.Equal(repo.Head(), preparation.Plan.Commit);
    }

    [Fact]
    public void The_argument_is_taken_as_a_folder_when_it_is_one()
    {
        using var folder = new Folder();

        Assert.Equal(folder.Path, new RunCommand.Settings { Repo = folder.Path }.Folder);
        Assert.Null(new RunCommand.Settings { Repo = "acme/app" }.Folder);

        // --path wins and is never guessed at, which is what a shell verb passes.
        Assert.Equal(folder.Path, new RunCommand.Settings { LocalPath = folder.Path }.Folder);
    }

    /// <summary>
    /// Workspace ids for repositories must not move: the variant only joins the hash when there is
    /// one, or an update would orphan every checkout on the machine and clone them all again.
    /// </summary>
    [Fact]
    public void An_id_for_a_repository_is_what_it_always_was()
    {
        Assert.Equal("acme__app__main-24bb71", WorkspaceStore.IdFor("https://github.com/acme/app", "main"));

        // A path yields a readable name rather than the whole path with underscores for separators.
        var id = WorkspaceStore.IdFor(@"C:\dev\projects\planner", "local");
        Assert.StartsWith("projects__planner__local-", id);
    }
}
