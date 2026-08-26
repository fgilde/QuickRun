using QuickRun.App;

namespace QuickRun.App.Tests;

/// <summary>
/// The crash log, which exists because a crash on the interface thread left nothing behind but an
/// entry in the Windows event log - and nobody can be asked to debug from there.
/// </summary>
public class CrashLogTests : IDisposable
{
    private readonly string? _previousHome = Environment.GetEnvironmentVariable("QUICKRUN_HOME");
    private readonly string _home = Path.Combine(Path.GetTempPath(), "quickrun-crash-" + Guid.NewGuid().ToString("n")[..8]);

    public CrashLogTests() => Environment.SetEnvironmentVariable("QUICKRUN_HOME", _home);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("QUICKRUN_HOME", _previousHome);
        try { Directory.Delete(_home, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void A_crash_is_written_and_can_be_read_back()
    {
        Assert.Null(CrashLog.Newest());

        var path = CrashLog.Write("unhandled", new InvalidOperationException("the icon could not be loaded"));

        Assert.NotNull(path);
        Assert.True(File.Exists(path));

        var newest = CrashLog.Newest();
        Assert.NotNull(newest);
        Assert.Equal(1, newest.Value.Count);
        Assert.Contains("the icon could not be loaded", newest.Value.Summary);

        // The file names the version and the command line, because a report without them is a guess.
        var written = File.ReadAllText(path!);
        Assert.Contains("QuickRun", written);
        Assert.Contains("command line:", written);
    }

    [Fact]
    public void Recording_a_crash_never_throws_even_with_nothing_to_record()
    {
        Assert.NotNull(CrashLog.Write("unhandled", null));
    }
}
