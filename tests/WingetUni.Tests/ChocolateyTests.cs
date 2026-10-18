using WingetUni.Core.Models;
using WingetUni.Providers.Chocolatey;

namespace WingetUni.Tests;

/// <summary>
/// Covers Chocolatey's output parsing and the parts of the provider that differ from the base
/// class: its exit-code handling and its command line.
/// </summary>
public sealed class ChocolateyTests
{
    private static Package Package(string id = "git") => new()
    {
        ManagerId = ChocolateyProvider.ProviderId,
        Id = id,
        Name = id,
    };

    [Fact]
    public void ReadsSearchRows()
    {
        var packages = ChocolateyOutputParser.ParseSearch(Fixtures.ReadLines("chocolatey-search.txt"), limit: 100);

        Assert.Equal(12, packages.Count);

        var first = packages[0];
        Assert.Equal("act-cli", first.Id);
        Assert.Equal("act-cli", first.Name);
        Assert.Equal("0.2.89", first.AvailableVersion);
        Assert.Equal("chocolatey", first.ManagerId);
        Assert.Equal("chocolatey", first.Source);
        Assert.Null(first.InstalledVersion);
    }

    [Fact]
    public void StopsSearchingAtTheRequestedLimit()
    {
        var packages = ChocolateyOutputParser.ParseSearch(Fixtures.ReadLines("chocolatey-search.txt"), limit: 3);

        Assert.Equal(3, packages.Count);
        Assert.Equal(["act-cli", "alera", "aptana-studio"], packages.Select(package => package.Id));
    }

    [Fact]
    public void ReadsInstalledRows()
    {
        var packages = ChocolateyOutputParser.ParseInstalled(["git|2.51.0", "7zip|24.09", "2 packages found."]);

        Assert.Equal(2, packages.Count);
        Assert.Equal("2.51.0", packages[0].InstalledVersion);
        Assert.True(packages[0].IsInstalled);
        Assert.False(packages[0].HasUpdate);
    }

    [Fact]
    public void ReadsOutdatedRowsAsUpdatablePackages()
    {
        var packages = ChocolateyOutputParser.ParseOutdated(Fixtures.ReadLines("chocolatey-outdated.txt"));

        Assert.Equal(5, packages.Count);

        var gh = packages[0];
        Assert.Equal("gh", gh.Id);
        Assert.Equal("2.101.0", gh.InstalledVersion);
        Assert.Equal("2.102.0", gh.UpdateVersion);
        Assert.True(gh.HasUpdate);
    }

    [Fact]
    public void SkipsPinnedPackagesWhenListingUpdates()
    {
        // A pinned package is deliberately held back, so it must not be offered as an update.
        string[] rows = ["held|1.0|2.0|true", "free|1.0|2.0|false"];

        var packages = ChocolateyOutputParser.ParseOutdated(rows);

        Assert.Equal("free", Assert.Single(packages).Id);
    }

    [Fact]
    public void ReadsPackageDetails()
    {
        var details = ChocolateyOutputParser.ParseDetails("chocolatey", Fixtures.ReadLines("chocolatey-info.txt"));

        Assert.NotNull(details);
        Assert.Equal("Chocolatey", details.Name);
        Assert.Equal("2.7.4", details.Version);
        Assert.Equal("https://github.com/chocolatey/choco", details.Homepage);
        Assert.Equal("https://raw.githubusercontent.com/chocolatey/choco/master/LICENSE", details.LicenseUrl);
        Assert.Equal("8/19/2026", details.ReleaseDate);
        Assert.Equal(
            ["nuget", "apt-get", "machine", "repository", "chocolatey"],
            details.Tags);
    }

    [Fact]
    public void KeepsTheChocolateyFieldBlockWhole()
    {
        var details = ChocolateyOutputParser.ParseDetails("chocolatey", Fixtures.ReadLines("chocolatey-info.txt"));

        Assert.NotNull(details);

        // The fields are indented by one space and the description by two, so a block read
        // without removing the common indent collapses to a single field. Counting the fields
        // is what proves the whole listing was read.
        Assert.Equal(17, details.Fields.Count);
        Assert.StartsWith("Chocolatey is a package manager for Windows", details.Description);
        Assert.Contains("Software License", details.Fields.Keys);
        Assert.Equal(
            ["Documentation: https://docs.chocolatey.org/en-us/",
             "Issues: https://github.com/chocolatey/choco/issues",
             "Mailing List: https://groups.google.com/forum/#!forum/chocolatey"],
            details.Documentation);
    }

    [Fact]
    public void ReturnsNoDetailsForAnEmptyListing()
    {
        Assert.Null(ChocolateyOutputParser.ParseDetails("missing", []));
        Assert.Null(ChocolateyOutputParser.ParseDetails("missing", ["", "   "]));
    }

    [Fact]
    public void TreatsARestartRequestAsASuccessfulInstall()
    {
        var runner = new StubProcessRunner().Respond(StubProcessRunner.Exited(3010, "installed, reboot pending"));
        var provider = new ChocolateyProvider(runner);

        var result = Run(provider, PackageOperation.Install, Package(), runner);

        Assert.Equal(3010, result.ExitCode);
        Assert.True(result.Success);
        Assert.Contains("restart", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TreatsAStartedRestartAsASuccessfulInstall()
    {
        var runner = new StubProcessRunner().Respond(StubProcessRunner.Exited(1641));
        var provider = new ChocolateyProvider(runner);

        var result = Run(provider, PackageOperation.Install, Package(), runner);

        Assert.True(result.Success);
    }

    [Fact]
    public void ReportsAFailedOperationWithTheToolsOwnWords()
    {
        var runner = new StubProcessRunner().Respond(StubProcessRunner.Exited(1, "Access is denied."));
        var provider = new ChocolateyProvider(runner);

        var result = Run(provider, PackageOperation.Install, Package(), runner);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Access is denied.", result.Message);
    }

    [Fact]
    public void ReportsAMissingToolWithoutThrowing()
    {
        var runner = new StubProcessRunner().Respond(StubProcessRunner.CouldNotStart("choco was not found"));
        var provider = new ChocolateyProvider(runner);

        var result = Run(provider, PackageOperation.Install, Package(), runner);

        Assert.False(result.Success);
        Assert.Contains("choco was not found", result.Message);
    }

    [Fact]
    public void BuildsAnUnattendedInstallCommand()
    {
        var runner = new StubProcessRunner().Respond(StubProcessRunner.Silent());
        var provider = new ChocolateyProvider(runner);
        var options = new InstallOptions { Source = "chocolatey", Version = "2.51.0", CustomArguments = "/S" };

        _ = Run(provider, PackageOperation.Install, Package(), runner, options);

        Assert.Equal(
            ["install", "git", "--no-progress", "--yes", "--source", "chocolatey", "--version", "2.51.0", "--install-arguments", "/S"],
            runner.LastRequest.Arguments);
    }

    [Fact]
    public void DoesNotForwardInstallerArgumentsWhenRemoving()
    {
        // Chocolatey rejects --install-arguments on uninstall, so it must not be appended.
        var runner = new StubProcessRunner().Respond(StubProcessRunner.Silent());
        var provider = new ChocolateyProvider(runner);

        _ = Run(provider, PackageOperation.Uninstall, Package(), runner, new InstallOptions { CustomArguments = "/S" });

        Assert.Equal(["uninstall", "git", "--no-progress", "--yes"], runner.LastRequest.Arguments);
    }

    private static OperationResult Run(
        ChocolateyProvider provider,
        PackageOperation operation,
        Package package,
        StubProcessRunner runner,
        InstallOptions? options = null) =>
        provider.ExecuteAsync(operation, package, options ?? InstallOptions.Default).GetAwaiter().GetResult();
}
