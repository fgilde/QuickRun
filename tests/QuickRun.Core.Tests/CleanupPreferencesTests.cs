using QuickRun.Core.Config;

namespace QuickRun.Core.Tests;

/// <summary>
/// How long a checkout is kept, remembered between runs.
/// <para>
/// A number somebody chooses, so what matters is that every answer they can give means what they
/// meant: a small number is theirs to pick, zero means "leave my disk alone", and a file they edited
/// by hand is read as written.
/// </para>
/// </summary>
public class CleanupPreferencesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("quickrun-cleanup").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Nothing_saved_means_a_month()
    {
        Assert.Equal(CleanupPreferences.DefaultDays, new CleanupPreferences(_root).RemoveAfterDays);

        // And asking wrote nothing: a machine nobody has configured keeps a clean directory.
        Assert.False(File.Exists(new CleanupPreferences(_root).Path));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(30)]
    [InlineData(365)]
    public void A_number_somebody_chose_is_the_number(int days)
    {
        new CleanupPreferences(_root).SetRemoveAfterDays(days);

        Assert.Equal(days, new CleanupPreferences(_root).RemoveAfterDays);
    }

    /// <summary>
    /// Off has to be reachable, and it has to survive being written down: null here is "remove
    /// nothing", and a run that reads it must not fall back to the default and start deleting.
    /// </summary>
    [Fact]
    public void Zero_means_nothing_is_removed()
    {
        var preferences = new CleanupPreferences(_root);

        preferences.SetRemoveAfterDays(null);
        Assert.Null(new CleanupPreferences(_root).RemoveAfterDays);

        preferences.SetRemoveAfterDays(0);
        Assert.Null(new CleanupPreferences(_root).RemoveAfterDays);

        // And back on again.
        preferences.SetRemoveAfterDays(14);
        Assert.Equal(14, new CleanupPreferences(_root).RemoveAfterDays);
    }

    [Theory]
    [InlineData("remove-after-days = 7", 7)]
    [InlineData("remove-after-days=2", 2)]
    [InlineData("REMOVE-AFTER-DAYS = 90", 90)]
    [InlineData("remove-after-days = 0", null)]
    [InlineData("remove-after-days = -5", null)]
    public void A_file_edited_by_hand_is_read_as_written(string line, int? expected)
    {
        var preferences = new CleanupPreferences(_root);
        File.WriteAllText(preferences.Path, $"# a comment\n\n{line}\n");

        Assert.Equal(expected, preferences.RemoveAfterDays);
    }

    /// <summary>
    /// Nonsense is not a licence to delete: whatever it says, it is not a smaller number than the
    /// default, so the answer is the default.
    /// </summary>
    [Fact]
    public void Something_unreadable_is_the_default()
    {
        var preferences = new CleanupPreferences(_root);
        File.WriteAllText(preferences.Path, "remove-after-days = soon\n");

        Assert.Equal(CleanupPreferences.DefaultDays, preferences.RemoveAfterDays);
    }
}
