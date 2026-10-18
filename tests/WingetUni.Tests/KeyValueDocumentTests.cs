using WingetUni.Core.Parsing;

namespace WingetUni.Tests;

public sealed class KeyValueDocumentTests
{
    [Fact]
    public void ReadsTopLevelValues()
    {
        var document = KeyValueDocument.Parse(Fixtures.ReadLines("winget-show.txt"));

        Assert.Equal("0.13.1", document.GetValue("Version"));
        Assert.Equal("Astral Software Inc.", document.GetValue("Publisher"));
        Assert.Equal("uv", document.GetValue("Moniker"));
        Assert.Equal("Apache-2.0 or MIT", document.GetValue("License"));
    }

    [Fact]
    public void KeepsAProseBlockIntact()
    {
        var document = KeyValueDocument.Parse(Fixtures.ReadLines("winget-show.txt"));

        var notes = document.GetText("Release Notes");

        Assert.NotNull(notes);
        Assert.Contains("Released on 2026-10-14.", notes);
        Assert.Contains("Performance", notes);
        Assert.Contains("- Reduce uv's binary size", notes);
    }

    [Fact]
    public void FollowsNestingToFourLevels()
    {
        var document = KeyValueDocument.Parse(Fixtures.ReadLines("winget-show.txt"));

        var installer = Assert.Single(document.Entries, entry => entry.Key == "Installer");
        Assert.Equal("portable (zip)", installer.ChildValue("Installer Type"));
        Assert.Equal("true", installer.ChildValue("Offline Distribution Supported"));

        var dependencies = installer.FindChild("Dependencies");
        Assert.NotNull(dependencies);

        var packageDependencies = Assert.Single(dependencies.Children);
        Assert.Equal("- Package Dependencies", packageDependencies.Key);
        Assert.Equal(["Microsoft.VCRedist.2015+.x64"], packageDependencies.Lines);
    }

    [Fact]
    public void ReadsASingleLineBlockAsAValue()
    {
        var document = KeyValueDocument.Parse(Fixtures.ReadLines("winget-show.txt"));

        Assert.Equal("python", document.GetValue("Tags"));
        Assert.Empty(document.GetChildren("Tags"));
    }

    [Fact]
    public void ReadsADocumentationSectionAsChildren()
    {
        var document = KeyValueDocument.Parse(Fixtures.ReadLines("winget-show.txt"));

        var link = Assert.Single(document.GetChildren("Documentation"));

        Assert.Equal("Docs", link.Key);
        Assert.Equal("https://docs.astral.sh/uv/", link.Value);
    }

    [Fact]
    public void ReportsMissingKeysAsNull()
    {
        var document = KeyValueDocument.Parse(Fixtures.ReadLines("winget-show.txt"));

        Assert.Null(document.GetValue("Not A Real Field"));
        Assert.Null(document.GetText("Not A Real Field"));
        Assert.Empty(document.GetBlockLines("Not A Real Field"));
        Assert.Empty(document.GetChildren("Not A Real Field"));
    }

    [Fact]
    public void RejectsEmptyInput()
    {
        var document = KeyValueDocument.Parse([]);

        Assert.Empty(document.Entries);
    }

    [Fact]
    public void KeepsALineThatHasNoColon()
    {
        var document = KeyValueDocument.Parse(["Name", "Version: 1.0"]);

        Assert.Equal("Name", document.Entries[0].Key);
        Assert.Equal(string.Empty, document.Entries[0].Value);
        Assert.Equal("Version", document.Entries[1].Key);
    }
}
