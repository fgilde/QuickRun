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
}
