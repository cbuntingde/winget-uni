using WingetUni.Core.Models;
using WingetUni.Core.Parsing;
using WingetUni.Providers.WinGet;

namespace WingetUni.Tests;

public sealed class WingetOutputParserTests
{
    [Fact]
    public void MapsSearchResultsToPackages()
    {
        var table = TextTable.Parse(Fixtures.ReadLines("winget-search.txt"));

        var packages = WingetOutputParser.ParseSearch(table);

        Assert.Equal(40, packages.Count);

        var first = packages[0];
        Assert.Equal("winget", first.ManagerId);
        Assert.Equal("Git.Git", first.Id);
        Assert.Equal("Git", first.Name);
        Assert.Equal("2.55.0.5", first.AvailableVersion);
        Assert.Equal("winget", first.Source);
        Assert.Null(first.InstalledVersion);
        Assert.Null(first.Match);
        Assert.False(first.IsInstalled);
        Assert.False(first.HasUpdate);
    }

    [Fact]
    public void ReadsTheMatchColumnOnlyWhenTheSearchReportedOne()
    {
        var table = TextTable.Parse(Fixtures.ReadLines("winget-search.txt"));

        var packages = WingetOutputParser.ParseSearch(table);

        Assert.Equal("Tag: git", packages.First(package => package.Id == "4nrry.OpenCherryDesktop").Match);
        Assert.All(packages.Where(package => package.Match is not null), package => Assert.StartsWith("Tag: ", package.Match));
    }

    [Fact]
    public void TreatsAnUpdateAsTheManagersOwnDecision()
    {
        var table = TextTable.Parse(Fixtures.ReadLines("winget-list.txt"));

        var packages = WingetOutputParser.ParseInstalled(table);

        // Winget reports this installed version as "< 20.0.1.256", which cannot be compared
        // with the offered version. The presence of an available version is the update signal.
        var updatable = packages.First(package => package.Id == "IObit.AdvancedSystemCare");
        Assert.Equal("< 20.0.1.256", updatable.InstalledVersion);
        Assert.Equal("20.0.1.256", updatable.UpdateVersion);
        Assert.True(updatable.IsInstalled);
        Assert.True(updatable.HasUpdate);
    }

    [Fact]
    public void LeavesTheUpdateVersionUnsetWhenThereIsNoUpdate()
    {
        var table = TextTable.Parse(Fixtures.ReadLines("winget-list.txt"));

        var packages = WingetOutputParser.ParseInstalled(table);

        var current = packages.First(package => package.Name == "AV1 Video Extension");
        Assert.Equal("2.0.35.0", current.InstalledVersion);
        Assert.Null(current.UpdateVersion);
        Assert.False(current.HasUpdate);
        Assert.Null(current.Source);
    }

    [Fact]
    public void MapsUpgradeListingsToInstalledPackagesWithAnUpdate()
    {
        var table = TextTable.Parse(Fixtures.ReadLines("winget-upgrade.txt"));

        var packages = WingetOutputParser.ParseInstalled(table);

        Assert.Equal(11, packages.Count);

        var terminal = packages.First(package => package.Id == "Microsoft.WindowsTerminal");
        Assert.Equal("1.24.12741.0", terminal.InstalledVersion);
        Assert.Equal("1.25.2733.0", terminal.UpdateVersion);
        Assert.True(terminal.HasUpdate);
    }

    [Fact]
    public void SkipsRowsThatCarryNoPackageId()
    {
        string[] lines =
        [
            "Name    Id    Version",
            "--------------------",
            "Alpha   A     1.0",
            "Orphan        2.0",
        ];

        var packages = WingetOutputParser.ParseSearch(TextTable.Parse(lines));

        Assert.Equal("A", Assert.Single(packages).Id);
    }

    [Fact]
    public void ReadsPackageDetails()
    {
        var details = WingetOutputParser.ParseDetails("astral-sh.uv", "uv", Fixtures.ReadLines("winget-show.txt"));

        Assert.NotNull(details);
        Assert.Equal("astral-sh.uv", details.Id);
        Assert.Equal("uv", details.Name);
        Assert.Equal("0.13.1", details.Version);
        Assert.Equal("Astral Software Inc.", details.Publisher);
        Assert.Equal("https://astral.sh/", details.PublisherUrl);
        Assert.Equal("https://github.com/astral-sh/uv/issues", details.PublisherSupportUrl);
        Assert.Equal("uv", details.Moniker);
        Assert.Equal("https://github.com/astral-sh/uv", details.Homepage);
        Assert.Equal("Apache-2.0 or MIT", details.License);
        Assert.Equal("portable (zip)", details.InstallerType);
        Assert.Equal(
            "https://github.com/astral-sh/uv/releases/download/0.13.1/uv-x86_64-pc-windows-msvc.zip",
            details.InstallerUrl);
        Assert.Equal(
            "3b2661a463a5baea7f5076ea2b25ab71d77f36a6c0d809e5350651792f80a672",
            details.InstallerSha256);
        Assert.Equal("2026-10-14", details.ReleaseDate);
        Assert.Equal(["python"], details.Tags);
        Assert.Equal(["Docs: https://docs.astral.sh/uv/"], details.Documentation);
    }

    [Fact]
    public void KeepsEveryReportedFieldIncludingNestedOnes()
    {
        var details = WingetOutputParser.ParseDetails("astral-sh.uv", "uv", Fixtures.ReadLines("winget-show.txt"));

        Assert.NotNull(details);
        Assert.Equal("0.13.1", details.Fields["Version"]);
        Assert.Equal("Copyright (c) 2026 Astral Software Inc.", details.Fields["Copyright"]);
        Assert.Equal("portable (zip)", details.Fields["Installer.Installer Type"]);
        Assert.Equal("2026-10-14", details.Fields["Installer.Release Date"]);

        // The "Found uv [astral-sh.uv]" banner is not a field.
        Assert.DoesNotContain("Found", details.Fields.Keys);
    }

    [Fact]
    public void FallsBackToTheRequestedNameWhenTheListingHasNone()
    {
        string[] lines = ["Version: 1.2.3", "Publisher: Someone"];

        var details = WingetOutputParser.ParseDetails("Some.Package", "Some Package", lines);

        Assert.NotNull(details);
        Assert.Equal("Some Package", details.Name);
        Assert.Equal("1.2.3", details.Version);
    }

    [Fact]
    public void ReturnsNoDetailsForAnEmptyListing()
    {
        Assert.Null(WingetOutputParser.ParseDetails("Some.Package", "Some Package", []));
        Assert.Null(WingetOutputParser.ParseDetails("Some.Package", null, ["", "   "]));
    }

    [Fact]
    public void KeepsWhatAManagerReportsEvenWhenTheHeadingIsUnknown()
    {
        // An unrecognized heading must not shift the columns that follow it.
        string[] lines =
        [
            "Name    Id    Version    Weird New Column    Source",
            "--------------------------------------------------------",
            "Alpha   A     1.0        something           winget",
        ];

        var packages = WingetOutputParser.ParseSearch(TextTable.Parse(lines));

        var package = Assert.Single(packages);
        Assert.Equal("A", package.Id);
        Assert.Equal("Alpha", package.Name);
        Assert.Equal("1.0", package.AvailableVersion);
    }

    [Fact]
    public void ReportsTheUpdateVersionSeparatelyFromTheInstalledVersion()
    {
        var package = new Package
        {
            ManagerId = "winget",
            Id = "A",
            Name = "Alpha",
            InstalledVersion = "1.0",
            UpdateVersion = "2.0",
        };

        Assert.True(package.HasUpdate);
        Assert.Equal("winget:A", package.Key);
    }
}
