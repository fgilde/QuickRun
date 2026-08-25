using QuickRun.App.Ui;

namespace QuickRun.App.Tests;

/// <summary>
/// Whether the window can host the local web UI. The answer is machine-dependent, so the test
/// asserts the contract rather than the outcome: it must answer without throwing, and it must be
/// false wherever a system WebView cannot exist.
/// </summary>
public class EmbeddedBrowserTests
{
    [Fact]
    public void Availability_can_always_be_asked()
    {
        var available = EmbeddedBrowser.Available();

        if (!OperatingSystem.IsWindows()) Assert.False(available);
    }

    /// <summary>The opt-out has to work, or a broken WebView leaves no way back to the native view.</summary>
    [Fact]
    public void The_opt_out_wins()
    {
        var before = Environment.GetEnvironmentVariable("QUICKRUN_NO_WEBVIEW");
        try
        {
            Environment.SetEnvironmentVariable("QUICKRUN_NO_WEBVIEW", "1");
            Assert.False(EmbeddedBrowser.Available());
        }
        finally
        {
            Environment.SetEnvironmentVariable("QUICKRUN_NO_WEBVIEW", before);
        }
    }
}
