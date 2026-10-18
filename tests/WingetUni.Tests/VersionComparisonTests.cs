using WingetUni.Core.Versions;

namespace WingetUni.Tests;

public sealed class VersionComparisonTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.4")]
    [InlineData("2.0", "10.0")]
    [InlineData("1.2", "1.2.1")]
    [InlineData("0.9", "1.0")]
    [InlineData("1.0.0-beta", "1.0.0")]
    public void OrdersTheOlderVersionFirst(string older, string newer)
    {
        Assert.True(VersionComparison.Compare(older, newer) < 0);
        Assert.True(VersionComparison.Compare(newer, older) > 0);
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("2.55.0.5", "2.55.0.5")]
    [InlineData("1.0", "1.0.0")]
    public void ReportsEquivalentVersionsAsEqual(string left, string right)
    {
        Assert.Equal(0, VersionComparison.Compare(left, right));
    }

    [Fact]
    public void OrdersNumericallyRatherThanAlphabetically()
    {
        // A plain string comparison would put "10.0" before "9.0".
        Assert.True(VersionComparison.Compare("9.0", "10.0") < 0);
    }

    [Fact]
    public void OrdersAMissingVersionBeforeAPresentOne()
    {
        Assert.True(VersionComparison.Compare(null, "1.0") < 0);
        Assert.True(VersionComparison.Compare("1.0", null) > 0);
        Assert.Equal(0, VersionComparison.Compare(null, null));
        Assert.True(VersionComparison.Compare(string.Empty, "1.0") < 0);
    }

    [Fact]
    public void GivesADefinedAnswerForVersionsThatAreNotNumeric()
    {
        // Winget reports "Unknown" for packages with no recorded version, so a comparison
        // must still return an answer instead of failing.
        Assert.NotEqual(0, VersionComparison.Compare("Unknown", "1.0"));
        Assert.Equal(0, VersionComparison.Compare("Unknown", "unknown"));
    }

    [Fact]
    public void IgnoresTheWingetUpperBoundMarker()
    {
        // Winget writes "< 20.0.1.256" when it cannot order the installed version.
        Assert.Equal(0, VersionComparison.Compare("< 20.0.1.256", "20.0.1.256"));
    }
}
