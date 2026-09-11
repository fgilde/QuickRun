using QuickRun.Core.Config;

namespace QuickRun.Core.Tests;

/// <summary>
/// Whether a window QuickRun opens stays above the others - remembered between runs.
/// <para>
/// A file somebody may open, so what it has to survive is somebody having opened it: a comment, a
/// blank line, a different spelling of yes, and the file not being there at all - which is the
/// state every machine starts in.
/// </para>
/// </summary>
public class WindowPreferencesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("quickrun-windows").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Nothing_saved_means_an_ordinary_window()
    {
        Assert.False(new WindowPreferences(_root).AlwaysOnTop);

        // And asking did not write anything: a machine that never opens the settings keeps a clean
        // directory.
        Assert.False(File.Exists(new WindowPreferences(_root).Path));
    }

    [Fact]
    public void What_was_switched_on_is_still_on_next_time()
    {
        new WindowPreferences(_root).SetAlwaysOnTop(true);

        Assert.True(new WindowPreferences(_root).AlwaysOnTop);

        new WindowPreferences(_root).SetAlwaysOnTop(false);

        Assert.False(new WindowPreferences(_root).AlwaysOnTop);
    }

    [Theory]
    [InlineData("always-on-top = yes", true)]
    [InlineData("always-on-top=true", true)]
    [InlineData("always-on-top = ON", true)]
    [InlineData("ALWAYS-ON-TOP = 1", true)]
    [InlineData("always-on-top = no", false)]
    [InlineData("always-on-top = maybe", false)]
    [InlineData("something-else = yes", false)]
    [InlineData("", false)]
    public void A_file_edited_by_hand_is_read_as_written(string line, bool expected)
    {
        var preferences = new WindowPreferences(_root);

        File.WriteAllText(preferences.Path,
            $"# a comment\n\n   \n{line}\n");

        Assert.Equal(expected, preferences.AlwaysOnTop);
    }

    /// <summary>
    /// The file is written with its own explanation, because the person most likely to open it is
    /// the person wondering what it does.
    /// </summary>
    [Fact]
    public void The_file_says_what_it_is_for()
    {
        var preferences = new WindowPreferences(_root);
        preferences.SetAlwaysOnTop(true);

        var text = File.ReadAllText(preferences.Path);

        Assert.Contains("#", text);
        Assert.Contains("always-on-top = yes", text);
    }
}
