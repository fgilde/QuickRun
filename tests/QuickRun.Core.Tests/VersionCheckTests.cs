using QuickRun.Core.Requires;

namespace QuickRun.Core.Tests;

public class VersionCheckTests
{
    [Theory]
    [InlineData("10.0.300", ">=9.0", true)]
    [InlineData("9.0.205", ">=9.0", true)]
    [InlineData("8.0.404", ">=9.0", false)]
    [InlineData("24.13.1", ">20", true)]
    [InlineData("20.0.0", ">20", false)]
    [InlineData("3.12.1", "<=3.12", false)]
    [InlineData("3.11.9", "<=3.12", true)]
    [InlineData("1.2.3", "=1.2.3", true)]
    [InlineData("1.2.4", "=1.2.3", false)]
    [InlineData("1.2.3", "1.2.3", true)]
    [InlineData("1.2.3", null, true)]
    [InlineData(null, ">=1.0", false)]
    public void Satisfies_compares_dotted_versions(string? found, string? range, bool expected)
        => Assert.Equal(expected, VersionCheck.Satisfies(found, range));

    [Theory]
    [InlineData("v24.13.1", "24.13.1")]
    [InlineData("Python 3.12.1", "3.12.1")]
    [InlineData("git version 2.51.2.windows.1", "2.51.2")]
    [InlineData("Docker version 27.3.1, build ce12230", "27.3.1")]
    [InlineData("no digits here", null)]
    public void Extract_pulls_the_first_dotted_version(string text, string? expected)
        => Assert.Equal(expected, VersionCheck.Extract(text));

    /// <summary>
    /// Ten is more than nine, which a comparison on text would get wrong.
    /// <para>
    /// This decides whether a release is offered at all: the check is "is the published version
    /// greater than mine", and the next one after 0.9.9 is 0.9.10. Compared as text that is smaller,
    /// and every machine on 0.9.9 would be told it is current for ever.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("0.9.10", "0.9.9", true)]
    [InlineData("0.9.9", "0.9.10", false)]
    [InlineData("0.10.0", "0.9.99", true)]
    [InlineData("1.0.0", "0.9.10", true)]
    [InlineData("0.9.9", "0.9.9", false)]
    public void A_double_digit_release_is_newer_than_a_single_digit_one(string published, string mine, bool newer)
        => Assert.Equal(newer, VersionCheck.Satisfies(published, ">" + mine));
}
