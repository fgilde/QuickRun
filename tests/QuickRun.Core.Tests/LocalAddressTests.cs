using QuickRun.Core.Run;

namespace QuickRun.Core.Tests;

public class LocalAddressTests
{
    [Theory]
    [InlineData("info: Now listening on: http://localhost:5082", "http://localhost:5082")]
    [InlineData("  Local:   http://127.0.0.1:5173/", "http://127.0.0.1:5173/")]
    [InlineData("Running on all addresses (http://0.0.0.0:7860)", "http://localhost:7860")]
    public void An_address_a_server_printed_is_recognised(string line, string expected) =>
        Assert.Equal(expected, LocalAddress.In(line));

    [Theory]
    // Every one of these appeared in a real build log. None of them is where the app is running.
    [InlineData("warning NU1902: see https://github.com/advisories/GHSA-pgww-w46g-26qg")]
    [InlineData("install: https://dotnet.microsoft.com/download")]
    [InlineData("Buildvorgang wird ausgefuehrt...")]
    public void Anything_that_is_not_loopback_is_ignored(string line) =>
        Assert.Null(LocalAddress.In(line));

    [Fact]
    public void Trailing_punctuation_is_not_part_of_the_address()
    {
        Assert.Equal("http://localhost:3000", LocalAddress.In("ready at http://localhost:3000."));
        Assert.Equal("http://localhost:8080", LocalAddress.In("(http://localhost:8080)"));
    }

    /// <summary>
    /// In a whole log the newest address is the true one: a dev server prints its port again after
    /// rebinding, and the first line may be from the build that preceded it.
    /// </summary>
    [Fact]
    public void The_last_address_in_a_log_wins() =>
        Assert.Equal("http://localhost:7861", LocalAddress.Last(
            """
            Downloading from https://github.com/advisories/GHSA-xxxx
            Running on local URL:  http://127.0.0.1:7860
            Port taken, retrying
            Running on local URL:  http://localhost:7861
            """));

    [Fact]
    public void A_log_without_a_local_address_yields_nothing() =>
        Assert.Null(LocalAddress.Last("""
            see https://dotnet.microsoft.com/download
            build succeeded
            """));
}
