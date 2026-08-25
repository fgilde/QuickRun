using QuickRun.App.Daemon;

namespace QuickRun.App.Tests;

/// <summary>
/// The two settings the local UI offers. Only the reading side is tested: switching them writes to
/// the registry, the login items or the user's PATH, and a test suite has no business doing that to
/// the machine it runs on.
/// </summary>
public class SystemSettingsTests
{
    [Fact]
    public void Autostart_says_where_it_would_be_written_either_way()
    {
        var status = SystemIntegration.Autostart();

        // Whether it is on or off, the detail has to name the place - that is what makes it possible
        // to undo by hand, and what tells a stale entry apart from a missing one.
        Assert.False(string.IsNullOrWhiteSpace(status.Detail));
        if (!status.Enabled) Assert.False(status.Stale);
    }

    [Fact]
    public void Reading_the_autostart_state_does_not_change_it()
    {
        var first = SystemIntegration.Autostart();
        var second = SystemIntegration.Autostart();

        Assert.Equal(first.Enabled, second.Enabled);
        Assert.Equal(first.Detail, second.Detail);
    }

    [Fact]
    public void The_terminal_setting_names_the_directory_it_needs()
    {
        var status = SystemIntegration.PathState();

        Assert.False(string.IsNullOrWhiteSpace(status.Directory));
        Assert.False(string.IsNullOrWhiteSpace(status.Detail));

        // On Windows the directory is the one the executable sits in; elsewhere it is the bin
        // directory a link goes into. Either way it is an absolute path.
        Assert.True(Path.IsPathRooted(status.Directory), status.Directory);
    }
}
