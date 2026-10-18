using WingetUni.Providers;

namespace WingetUni.Tests;

public sealed class CliTextTests
{
    [Fact]
    public void SplitsColumnsOnRunsOfSpaces()
    {
        Assert.Equal(["Name", "Id", "Source"], CliText.SplitColumns("Name    Id    Source"));
    }

    [Fact]
    public void KeepsASingleSpaceInsideACell()
    {
        Assert.Equal(["Visual Studio Code", "XP9KHM4BK9FZ7Q"], CliText.SplitColumns("Visual Studio Code  XP9KHM4BK9FZ7Q"));
    }

    [Fact]
    public void DropsEmptyCells()
    {
        Assert.Equal(["Alpha", "A"], CliText.SplitColumns("   Alpha      A   "));
    }

    [Theory]
    [InlineData("pip 26.2.1 from C:\\Python314\\Lib\\site-packages\\pip", "26.2.1")]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("Chocolatey v2.7.4", "2.7.4")]
    [InlineData("Current Scoop version:", null)]
    [InlineData("no digits here", null)]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void ExtractsTheVersionToken(string? line, string? expected)
    {
        Assert.Equal(expected, CliText.VersionToken(line));
    }

    [Fact]
    public void DoesNotMistakeAPathForAVersion()
    {
        // The path after "from" starts with a letter, so it is not a version even though it
        // contains dots and backslashes.
        Assert.Equal("3.14.7", CliText.VersionToken("Python 3.14.7 (C:\\Python314)"));
    }

    [Theory]
    [InlineData("--------", true)]
    [InlineData("- - - -", true)]
    [InlineData("----  -------  ------", true)]
    [InlineData("Name", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("— —", false)]
    public void RecognisesARuleLine(string line, bool expected)
    {
        Assert.Equal(expected, CliText.IsRuleLine(line));
    }

    [Fact]
    public void ReadsPipeDelimitedRowsAndIgnoresOtherLines()
    {
        string[] lines =
        [
            "act-cli|0.2.89",
            "Chocolatey v2.7.4",
            "alera|0.107.1",
            "2 packages found.",
            "",
        ];

        var rows = CliText.SplitPipeRows(lines, 2);

        Assert.Equal(2, rows.Count);
        Assert.Equal(["act-cli", "0.2.89"], rows[0]);
        Assert.Equal(["alera", "0.107.1"], rows[1]);
    }

    [Fact]
    public void RejectsPipeRowsThatHaveTooFewFields()
    {
        var rows = CliText.SplitPipeRows(["gh|2.101.0|2.102.0|false", "partial|1.0"], 4);

        Assert.Equal(["gh", "2.101.0", "2.102.0", "false"], Assert.Single(rows));
    }

    [Fact]
    public void TrimsWhitespaceAroundPipeFields()
    {
        Assert.Equal(["a", "b"], CliText.SplitPipeRows(["a | b "], 2)[0]);
    }

    [Fact]
    public void FindsTheHeaderByHeadingName()
    {
        // Scoop prints a table of this shape, whose rule is not a plain run of dashes.
        string[] lines =
        [
            "Results from local buckets...",
            "",
            "Name    Version    Source",
            "----    -------    ------",
            "7zip    23.01      main",
            "git     2.42.0     main",
        ];

        var rows = CliText.ParseTable(lines, "Name", "Version");

        Assert.Equal(2, rows.Count);
        Assert.Equal("7zip", CliText.Cell(rows[0], "Name"));
        Assert.Equal("23.01", CliText.Cell(rows[0], "Version"));
        Assert.Equal("main", CliText.Cell(rows[0], "Source"));
    }

    [Fact]
    public void KeepsTheRowsBelowAFullHeaderWhenOnlySomeHeadingsAreKnown()
    {
        string[] lines =
        [
            "Name    Installed Version    Latest Version",
            "----    -----------------    --------------",
            "git     2.42.0               2.43.0",
        ];

        var all = CliText.ParseTable(lines, "Name", "Installed Version", "Latest Version");
        var partial = CliText.ParseTable(lines, "Name", "Latest Version");

        Assert.Equal("2.43.0", CliText.Cell(Assert.Single(all), "Latest Version"));
        Assert.Equal("2.43.0", CliText.Cell(Assert.Single(partial), "Latest Version"));
    }

    [Fact]
    public void IgnoresATrailingSentenceThatCollapsesToOneCell()
    {
        string[] lines =
        [
            "Name    Version",
            "----    -------",
            "7zip    23.01",
            "Everything is up to date.",
        ];

        var rows = CliText.ParseTable(lines, "Name", "Version");

        Assert.Equal("7zip", CliText.Cell(Assert.Single(rows), "Name"));
    }

    [Fact]
    public void ReturnsNothingWhenNoHeadingLineIsPresent()
    {
        Assert.Empty(CliText.ParseTable(["nothing here", "still nothing"], "Name", "Version"));
    }

    [Fact]
    public void ReadsACellAsNullWhenItIsAbsentOrBlank()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = "git",
            ["Source"] = string.Empty,
        };

        Assert.Equal("git", CliText.Cell(row, "name"));
        Assert.Null(CliText.Cell(row, "Source"));
        Assert.Null(CliText.Cell(row, "Missing"));
    }

    [Fact]
    public void JoinsCapturedLinesBackTogether()
    {
        Assert.Equal("one\ntwo", CliText.JoinLines(["one", "two"]));
    }
}
