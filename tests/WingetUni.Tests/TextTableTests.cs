using WingetUni.Core.Parsing;

namespace WingetUni.Tests;

public sealed class TextTableTests
{
    [Fact]
    public void ParsesInstalledListingFromWinget()
    {
        var table = TextTable.Parse(Fixtures.ReadLines("winget-list.txt"));

        Assert.Equal(["Name", "Id", "Version", "Available", "Source"], table.Headers);
        Assert.Equal(109, table.Rows.Count);

        var first = table.Rows[0];
        Assert.Equal("Advanced SystemCare", first[0]);
        Assert.Equal("IObit.AdvancedSystemCare", first[1]);
        Assert.Equal("< 20.0.1.256", first[2]);
        Assert.Equal("20.0.1.256", first[3]);
        Assert.Equal("winget", first[4]);
    }

    [Fact]
    public void KeepsRowsWhoseOptionalColumnsAreAbsent()
    {
        var table = TextTable.Parse(Fixtures.ReadLines("winget-list.txt"));

        // Registry and MSIX entries have neither an available version nor a source, so the
        // row is shorter than the header. That must read as empty cells, not as a parse error.
        var registryRow = table.Rows.First(row => row[1].StartsWith("ARP\\", StringComparison.Ordinal));

        Assert.Equal(string.Empty, registryRow[3]);
        Assert.Equal(string.Empty, registryRow[4]);
    }

    [Fact]
    public void ExcludesUpgradeSummaryFooter()
    {
        var table = TextTable.Parse(Fixtures.ReadLines("winget-upgrade.txt"));

        // The listing ends with "11 upgrades available.", which is a summary, not a package.
        Assert.Equal(11, table.Rows.Count);
        Assert.DoesNotContain(table.Rows, row => row[0].Contains("available", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ParsesSearchResultsThatHaveFourColumns()
    {
        var table = TextTable.Parse(Fixtures.ReadLines("winget-search-no-match-column.txt"));

        // A search without a query prints no Match column, so the offsets differ from the
        // five-column form used elsewhere.
        Assert.Equal(["Name", "Id", "Version", "Source"], table.Headers);
        Assert.Equal("Visual Studio Code", table.Rows[0][0]);
        Assert.Equal("XP9KHM4BK9FZ7Q", table.Rows[0][1]);
        Assert.Equal("Unknown", table.Rows[0][2]);
        Assert.Equal("msstore", table.Rows[0][3]);
    }

    [Fact]
    public void KeepsValuesThatAreWiderThanTheirHeading()
    {
        var table = TextTable.Parse(Fixtures.ReadLines("winget-search-no-match-column.txt"));

        var row = table.Rows.First(item => item[0].StartsWith("Visual Studio /", StringComparison.Ordinal));

        Assert.Equal("Visual Studio / Code for Command Palette", row[0]);
        Assert.Equal("15722UsefulApp.WorkspaceLauncherForVSCode", row[1]);
    }

    [Fact]
    public void ReturnsNoTableWhenTheCommandReportedNothing()
    {
        var table = TextTable.Parse(Fixtures.ReadLines("winget-no-results.txt"));

        Assert.Empty(table.Headers);
        Assert.Empty(table.Rows);
        Assert.False(table.HasRows);
    }

    [Fact]
    public void DerivesColumnOffsetsFromTheHeader()
    {
        // The same parser must handle a table whose offsets bear no relation to Winget's,
        // which is what keeps the offsets from being hard-coded.
        string[] lines =
        [
            "Name    Id    Source",
            "--------------------",
            "Alpha   A     winget",
            "Beta    BB    scoop",
        ];

        var table = TextTable.Parse(lines);

        Assert.Equal(["Name", "Id", "Source"], table.Headers);
        Assert.Equal(["Alpha", "A", "winget"], table.Rows[0]);
        Assert.Equal(["Beta", "BB", "scoop"], table.Rows[1]);
    }

    [Fact]
    public void IgnoresOutputThatPrecedesTheTable()
    {
        string[] lines =
        [
            "A source agreement was accepted.",
            "Name    Id",
            "----------------",
            "Alpha   A",
        ];

        var table = TextTable.Parse(lines);

        Assert.Equal(["Alpha", "A"], Assert.Single(table.Rows));
    }

    [Fact]
    public void ExcludesTheResultLimitFooter()
    {
        var table = TextTable.Parse(Fixtures.ReadLines("winget-search.txt"));

        // The listing ends with "<additional entries truncated due to result limit>", which is
        // long enough to reach into the second column but is not laid out on the columns.
        Assert.Equal(40, table.Rows.Count);
        Assert.DoesNotContain(table.Rows, row => row[0].Contains("truncated", StringComparison.Ordinal));
    }

    [Fact]
    public void KeepsRowsThatContainWideCharacters()
    {
        var table = TextTable.Parse(Fixtures.ReadLines("winget-search.txt"));

        // Winget pads by display width, so this row is two characters shorter than the header
        // even though it ends in the same column.
        var row = table.Rows.First(item => item[1] == "DuckStudio.ChineseGit");

        Assert.Equal("中文Git", row[0]);
        Assert.Equal("3.4", row[2]);
        Assert.Equal("Tag: git", row[3]);
        Assert.Equal("winget", row[4]);
    }

    [Fact]
    public void RejectsEmptyInput()
    {
        Assert.Empty(TextTable.Parse([]).Rows);
        Assert.Empty(TextTable.Parse([string.Empty, string.Empty]).Rows);
    }
}
