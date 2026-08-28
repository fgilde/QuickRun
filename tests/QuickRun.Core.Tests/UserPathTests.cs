using QuickRun.Core;
using QuickRun.Core.Process;

namespace QuickRun.Core.Tests;

/// <summary>
/// The PATH a run looks for its tools in.
/// <para>
/// A machine with dotnet, docker and node installed had QuickRun report all three as missing,
/// because an app started from the Finder inherits four directories and none of them is where any
/// of those live. These cover the merge itself, which is the part that decides what is found.
/// </para>
/// </summary>
public class UserPathTests
{
    private static string Join(params string[] entries) => string.Join(Path.PathSeparator, entries);

    [Fact]
    public void The_login_shell_comes_first_because_that_is_where_the_user_resolves_commands()
    {
        var merged = UserPath.Merge(
            current: Join("/usr/bin", "/bin"),
            login: Join("/opt/homebrew/bin", "/usr/bin", "/bin"),
            candidates: []);

        Assert.Equal(Join("/opt/homebrew/bin", "/usr/bin", "/bin"), merged);
    }

    [Fact]
    public void Nothing_the_process_already_had_is_lost()
    {
        var merged = UserPath.Merge(
            current: Join("/usr/bin", "/sbin"),
            login: Join("/opt/homebrew/bin"),
            candidates: []);

        Assert.Equal(Join("/opt/homebrew/bin", "/usr/bin", "/sbin"), merged);
    }

    [Fact]
    public void A_directory_is_listed_once_however_many_sources_name_it()
    {
        var merged = UserPath.Merge(
            current: Join("/usr/local/bin", "/usr/bin"),
            login: Join("/usr/local/bin", "/usr/bin"),
            candidates: ["/usr/local/bin"]);

        Assert.Equal(Join("/usr/local/bin", "/usr/bin"), merged);
    }

    [Fact]
    public void The_fallbacks_come_last_so_they_never_shadow_the_users_own_choice()
    {
        var merged = UserPath.Merge(
            current: Join("/usr/bin"),
            login: null,
            candidates: ["/opt/homebrew/bin", "/usr/local/share/dotnet"]);

        Assert.Equal(Join("/usr/bin", "/opt/homebrew/bin", "/usr/local/share/dotnet"), merged);
    }

    [Fact]
    public void Empty_and_blank_entries_are_not_directories()
    {
        var merged = UserPath.Merge(
            current: Join("/usr/bin", "", "  "),
            login: Join("", "/bin"),
            candidates: []);

        Assert.Equal(Join("/bin", "/usr/bin"), merged);
    }

    [Fact]
    public void Nothing_at_all_stays_nothing_rather_than_becoming_a_separator()
    {
        Assert.Equal("", UserPath.Merge(null, null, []));
    }

    [Theory]
    [InlineData("QUICKRUN_PATH=/usr/bin:/bin", "/usr/bin:/bin")]
    [InlineData("welcome to zsh\nQUICKRUN_PATH=/opt/homebrew/bin\n", "/opt/homebrew/bin")]
    [InlineData("  QUICKRUN_PATH=/bin  ", "/bin")]
    public void A_profile_that_talks_does_not_hide_the_answer(string output, string expected)
    {
        Assert.Equal(expected, UserPath.ReadProbe(output));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("nvm is not compatible with the npm config prefix option")]
    public void Output_without_the_marker_is_not_a_path(string? output)
    {
        Assert.Null(UserPath.ReadProbe(output));
    }

    /// <summary>
    /// The part that cannot be faked: on a Unix machine the login shell has to answer, because that
    /// answer is the whole fix. If the profile is never read, QuickRun is back to the four
    /// directories macOS hands a bundle.
    /// </summary>
    [Fact]
    public void On_unix_the_login_shell_answers_with_a_path()
    {
        if (OSKinds.Current == OSKind.Windows) return;

        var path = UserPath.LoginShellPath();

        Assert.NotNull(path);
        Assert.Contains("/bin", path);
    }

    /// <summary>
    /// A profile that prints must not stop QuickRun from starting.
    /// <para>
    /// Reading one stream to the end while the other fills its pipe buffer is a deadlock a timeout
    /// cannot break, because the wait never begins. nvm warns on every single shell, so this is the
    /// ordinary case, not an exotic one.
    /// </para>
    /// </summary>
    [Fact]
    public void A_shell_that_prints_a_lot_still_answers()
    {
        if (OSKinds.Current == OSKind.Windows) return;

        var shell = Fake("""
            #!/bin/sh
            # Far more than a pipe buffer holds, on both streams, before the answer.
            i=0
            while [ $i -lt 4000 ]; do
              echo "chatter chatter chatter chatter chatter chatter chatter chatter" >&2
              echo "chatter chatter chatter chatter chatter chatter chatter chatter"
              i=$((i + 1))
            done
            printf 'QUICKRUN_PATH=%s\n' "/opt/homebrew/bin:/usr/bin"
            """);

        Assert.Equal("/opt/homebrew/bin:/usr/bin", UserPath.LoginShellPath(shell));
    }

    /// <summary>A shell that never returns costs a second, not the startup.</summary>
    [Fact]
    public void A_shell_that_hangs_is_given_up_on()
    {
        if (OSKinds.Current == OSKind.Windows) return;

        var shell = Fake("""
            #!/bin/sh
            sleep 30
            """);

        var started = DateTime.UtcNow;
        var path = UserPath.LoginShellPath(shell, timeoutMs: 1_000);

        Assert.Null(path);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10), "it waited far too long");
    }

    /// <summary>A script standing in for a login shell: it is handed -lc and a command, and ignores both.</summary>
    private static string Fake(string script)
    {
        var path = Path.Combine(Path.GetTempPath(), $"qr-shell-{Guid.NewGuid():n}.sh");
        File.WriteAllText(path, script.ReplaceLineEndings("\n"));

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        return path;
    }

    [Fact]
    public void Adopting_never_shortens_the_path()
    {
        var before = Environment.GetEnvironmentVariable("PATH") ?? "";

        UserPath.Adopt();

        var after = Environment.GetEnvironmentVariable("PATH") ?? "";
        var had = before.Split(Path.PathSeparator).Where(e => e.Length > 0);

        Assert.All(had, entry => Assert.Contains(entry, after.Split(Path.PathSeparator)));
    }
}
