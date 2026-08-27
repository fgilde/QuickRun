using QuickRun.App.Daemon;

namespace QuickRun.App.Tests;

/// <summary>
/// Finding the browsers on this machine, which is the first half of getting the extension into one.
/// </summary>
public class BrowserInstallsTests
{
    [Fact]
    public void Every_browser_it_reports_is_one_it_actually_found()
    {
        foreach (var browser in BrowserInstalls.All())
        {
            Assert.False(string.IsNullOrWhiteSpace(browser.Name));
            Assert.Contains(browser.Family, new[] { "chromium", "firefox" });

            // The path is what launching it depends on, so a browser is only worth reporting when
            // it is there: an executable on Windows and Linux, an .app bundle on macOS.
            Assert.True(File.Exists(browser.Executable) || Directory.Exists(browser.Executable),
                $"{browser.Name} was reported at {browser.Executable}, which does not exist");

            Assert.Contains(browser.Extension, new[] { "installed", "connected", "missing" });
        }
    }

    /// <summary>
    /// An extension that has made a request is installed, whatever a profile directory shows - and
    /// for an unpacked build there is no predictable directory to look in at all.
    /// </summary>
    [Fact]
    public void An_extension_that_has_connected_counts_as_present()
    {
        BrowserInstalls.Remember("chrome-extension://abcdefghijklmnopabcdefghijklmnop");

        // Nothing to assert where no Chromium browser exists, which is a real case: a Linux CI
        // runner has none.
        Assert.All(BrowserInstalls.All().Where(browser => browser.Family == "chromium"),
            browser => Assert.NotEqual("missing", browser.Extension));
    }

    [Fact]
    public void A_store_link_is_only_offered_where_a_listing_exists()
    {
        foreach (var browser in BrowserInstalls.All())
        {
            if (browser.Store is null) continue;

            Assert.StartsWith("https://", browser.Store);

            // Chrome has no listing yet, so it must not be handed one - a button leading to a 404
            // is worse than the honest unpacked route.
            Assert.NotEqual("chrome", browser.Id);
        }
    }
}
