using QuickRun.App.Ui;

namespace QuickRun.App.Tests;

/// <summary>
/// Whether the window can host the local web UI.
/// <para>
/// The answer is machine-dependent, so these assert the contract rather than the outcome. The
/// contract changed when macOS and Linux got the same treatment as Windows: it used to be "false
/// anywhere but Windows", and this test held that in place - rightly, until the system WebView on
/// those platforms became something QuickRun uses. Whether WebKitGTK is installed cannot be known
/// without asking it to start, so the promise is now narrower: asking is always safe, the answer
/// does not wander, and the opt-out wins everywhere.
/// </para>
/// </summary>
public class EmbeddedBrowserTests
{
    /// <summary>
    /// Asking is safe and gives the same answer twice.
    /// <para>
    /// The important half is that it returns at all: this runs before any window exists, on three
    /// operating systems, and a machine with no WebView has to get an answer rather than an
    /// exception. The second call is there because this decides which interface the user gets - an
    /// answer that varies between two calls would mean a window that sometimes hosts the page and
    /// sometimes does not.
    /// </para>
    /// </summary>
    [Fact]
    public void Asking_is_safe_and_the_answer_is_stable()
    {
        var first = EmbeddedBrowser.Available();
        var second = EmbeddedBrowser.Available();

        Assert.Equal(first, second);
    }

    /// <summary>The opt-out has to work, or a broken WebView leaves no way back to the native view.</summary>
    [Fact]
    public void The_opt_out_wins()
    {
        var before = Environment.GetEnvironmentVariable("QUICKRUN_NO_WEBVIEW");
        try
        {
            Environment.SetEnvironmentVariable("QUICKRUN_NO_WEBVIEW", "1");

            // On every platform: this is the way back to the native view when a WebView misbehaves,
            // and it must not depend on which platform is misbehaving.
            Assert.False(EmbeddedBrowser.Available());
        }
        finally
        {
            Environment.SetEnvironmentVariable("QUICKRUN_NO_WEBVIEW", before);
        }
    }
}
