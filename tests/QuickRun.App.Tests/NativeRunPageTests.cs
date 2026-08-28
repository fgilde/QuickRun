using QuickRun.App.Ui;

namespace QuickRun.App.Tests;

/// <summary>
/// What the native window makes of the one field it asks with.
/// <para>
/// This window is the fallback where there is no system WebView - Linux without WebKitGTK, a build
/// that opts out, a WebView that fails to start - and it had no way to run a folder at all. It can
/// do something the page in a browser cannot: ask the file system instead of guessing from the shape
/// of the string.
/// </para>
/// </summary>
public class NativeRunPageTests
{
    [Fact]
    public void A_directory_that_is_there_is_read_as_a_folder()
    {
        var folder = Directory.CreateTempSubdirectory("quickrun-native-").FullName;

        try
        {
            Assert.True(RunPage.IsFolder(folder));

            // Trailing spaces come from pasting a path out of anything.
            Assert.True(RunPage.IsFolder($"  {folder}  "));
        }
        finally
        {
            Directory.Delete(folder);
        }
    }

    [Theory]
    [InlineData("acme/app")]
    [InlineData("https://github.com/acme/app")]
    [InlineData("git@github.com:acme/app.git")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_repository_is_not_read_as_a_folder(string? value) =>
        Assert.False(RunPage.IsFolder(value));

    /// <summary>
    /// A path that is not there is not a folder - it is a repository that will fail to check out,
    /// and the window says so before anyone presses Prepare.
    /// </summary>
    [Fact]
    public void A_path_that_is_not_there_is_not_a_folder()
    {
        var missing = Path.Combine(Path.GetTempPath(), "quickrun-not-here-" + Guid.NewGuid().ToString("n"));
        Assert.False(RunPage.IsFolder(missing));
    }

    /// <summary>
    /// Relative only when fully qualified: a directory that happens to sit beside whatever the
    /// daemon's working directory is must not turn "acme/app" into something local.
    /// </summary>
    [Fact]
    public void A_relative_name_is_never_a_folder_even_when_one_exists()
    {
        var here = Directory.GetCurrentDirectory();
        var made = Path.Combine(here, "acme");
        var mine = !Directory.Exists(made);

        if (mine) Directory.CreateDirectory(Path.Combine(made, "app"));

        try
        {
            Assert.False(RunPage.IsFolder("acme/app"));
        }
        finally
        {
            if (mine) Directory.Delete(made, recursive: true);
        }
    }

    /// <summary>
    /// The form for a repository: a branch and a token, and nothing about copying - a checkout has
    /// no original to leave alone.
    /// </summary>
    [Fact]
    public void A_repository_offers_a_branch_and_a_token()
    {
        var view = RunPage.Read("acme/app");

        Assert.False(view.Folder);
        Assert.True(view.ShowRepoFields);
        Assert.False(view.ShowCopy);
        Assert.Contains("checks it out", view.Explanation);
    }

    /// <summary>And the form for a folder: the copy switch, and no branch to check out.</summary>
    [Fact]
    public void A_folder_offers_the_copy_switch_and_no_branch()
    {
        var folder = Directory.CreateTempSubdirectory("quickrun-view-").FullName;

        try
        {
            var view = RunPage.Read(folder);

            Assert.True(view.Folder);
            Assert.True(view.ShowCopy);
            Assert.False(view.ShowRepoFields);
            Assert.Contains("runs where it lies", view.Explanation);
        }
        finally
        {
            Directory.Delete(folder);
        }
    }

    /// <summary>
    /// A path that is not there says so. Left as a repository it would be checked out and fail for a
    /// reason that has nothing to do with the typo.
    /// </summary>
    [Fact]
    public void A_path_that_is_not_there_is_said_to_be_missing()
    {
        var missing = Path.Combine(Path.GetTempPath(), "quickrun-typo-" + Guid.NewGuid().ToString("n"));
        var view = RunPage.Read(missing);

        Assert.False(view.Folder);
        Assert.Contains("not there", view.Explanation);

        // And a URL is never mistaken for a mistyped path.
        Assert.DoesNotContain("not there", RunPage.Read("https://github.com/acme/app").Explanation);
    }

    [Fact]
    public void An_empty_field_explains_both_things_it_takes()
    {
        var view = RunPage.Read("");

        Assert.False(view.Folder);
        Assert.True(view.ShowRepoFields);
        Assert.Contains("repository", view.Explanation);
        Assert.Contains("folder", view.Explanation);
    }
}
