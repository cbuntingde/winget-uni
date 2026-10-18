using WingetUni.Core.Abstractions;
using WingetUni.Core.Models;
using WingetUni.Providers.DotNetTool;
using WingetUni.Providers.Npm;
using WingetUni.Providers.Pip;
using WingetUni.Providers.PowerShellGallery;

namespace WingetUni.Tests;

/// <summary>
/// Exercises the providers that read JSON, fixed-width tables, and shell scripts: what they make
/// of recorded output, and the command lines and scripts they build.
/// </summary>
public sealed class ProviderParsingTests
{
    private static Package Package(string managerId, string id) => new()
    {
        ManagerId = managerId,
        Id = id,
        Name = id,
    };

    // pip ------------------------------------------------------------------------------------

    [Fact]
    public async Task PipReadsItsInstalledListings()
    {
        var runner = new StubProcessRunner().RespondWith([.. Fixtures.ReadLines("pip-list.json")]);
        var provider = new PipProvider(runner);

        var packages = await provider.ListInstalledAsync();

        Assert.Equal(50, packages.Count);
        Assert.Equal(["list", "--format=json"], runner.LastRequest.Arguments);
        Assert.All(packages, package => Assert.Equal(PipProvider.SourceName, package.Source));

        var pip = Assert.Single(packages, package => package.Id == "pip");
        Assert.Equal("26.2.1", pip.InstalledVersion);
    }

    [Fact]
    public async Task PipReadsItsOutdatedListing()
    {
        var runner = new StubProcessRunner().RespondWith([.. Fixtures.ReadLines("pip-outdated.json")]);
        var provider = new PipProvider(runner);

        var packages = await provider.ListUpgradableAsync();

        Assert.Equal(["list", "--outdated", "--format=json"], runner.LastRequest.Arguments);
        Assert.Equal(2, packages.Count);

        var pillow = packages.Single(package => package.Id == "pillow");
        Assert.Equal("12.3.0", pillow.InstalledVersion);
        Assert.Equal("13.0.0", pillow.UpdateVersion);
        Assert.True(pillow.HasUpdate);
    }

    [Fact]
    public async Task PipReadsPackageDetails()
    {
        var runner = new StubProcessRunner().RespondWith([.. Fixtures.ReadLines("pip-show.txt")]);
        var provider = new PipProvider(runner);

        var details = await provider.GetDetailsAsync(Package(PipProvider.ProviderId, "pip"));

        Assert.NotNull(details);
        Assert.Equal(["show", "pip"], runner.LastRequest.Arguments);
        Assert.Equal("pip", details.Name);
        Assert.Equal("26.2.1", details.Version);
        Assert.Equal("The PyPA recommended tool for installing Python packages.", details.Description);
        Assert.Equal("https://pip.pypa.io/", details.Homepage);
        Assert.Equal("MIT", details.License);
        Assert.Equal("The pip developers <distutils-sig@python.org>", details.Publisher);
        Assert.Null(details.Author);
    }

    [Fact]
    public async Task PipExplainsThatItCannotSearch()
    {
        var runner = new StubProcessRunner();
        var provider = new PipProvider(runner);

        var error = await Assert.ThrowsAsync<PackageManagerException>(() => provider.SearchAsync("requests", 10));

        Assert.Contains("no search command", error.Message);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task PipBuildsAnUnattendedInstallCommand()
    {
        var (runner, provider) = Pip();

        await provider.ExecuteAsync(PackageOperation.Install, Package(PipProvider.ProviderId, "requests"), InstallOptions.Default);

        Assert.Equal(
            ["install", "requests", "--no-input", "--disable-pip-version-check"],
            runner.LastRequest.Arguments);
    }

    [Fact]
    public async Task PipPinsAVersionAndHonoursAScope()
    {
        var (runner, provider) = Pip();
        var options = new InstallOptions { Version = "2.34.2", Scope = "user", CustomArguments = "--pre" };

        await provider.ExecuteAsync(PackageOperation.Install, Package(PipProvider.ProviderId, "requests"), options);

        Assert.Equal(
            ["install", "requests==2.34.2", "--no-input", "--disable-pip-version-check", "--user", "--pre"],
            runner.LastRequest.Arguments);
    }

    [Fact]
    public async Task PipAsksForAnUpgradeWhenUpdating()
    {
        var (runner, provider) = Pip();

        await provider.ExecuteAsync(PackageOperation.Update, Package(PipProvider.ProviderId, "requests"), InstallOptions.Default);

        Assert.Equal(
            ["install", "requests", "--upgrade", "--no-input", "--disable-pip-version-check"],
            runner.LastRequest.Arguments);
    }

    [Fact]
    public async Task PipConfirmsBeforeRemoving()
    {
        var (runner, provider) = Pip();

        await provider.ExecuteAsync(PackageOperation.Uninstall, Package(PipProvider.ProviderId, "requests"), InstallOptions.Default);

        Assert.Equal(["uninstall", "requests", "--yes", "--disable-pip-version-check"], runner.LastRequest.Arguments);
    }

    private static (StubProcessRunner Runner, PipProvider Provider) Pip()
    {
        var runner = new StubProcessRunner().Respond(StubProcessRunner.Silent());
        return (runner, new PipProvider(runner));
    }

    // npm ------------------------------------------------------------------------------------

    [Fact]
    public async Task NpmReadsItsSearchResults()
    {
        var runner = new StubProcessRunner().RespondWith([.. Fixtures.ReadLines("npm-search.json")]);
        var provider = new NpmProvider(runner);

        var packages = await provider.SearchAsync("vue", 5);

        Assert.Equal(3, packages.Count);
        Assert.Equal(["search", "--json", "--searchlimit=5", "vue"], runner.LastRequest.Arguments);

        var vue = packages[0];
        Assert.Equal("vue", vue.Id);
        Assert.Equal("3.5.44", vue.AvailableVersion);
        Assert.Equal(NpmProvider.SourceName, vue.Source);
    }

    [Fact]
    public async Task NpmDoesNotSearchForAnEmptyTerm()
    {
        var runner = new StubProcessRunner();
        var provider = new NpmProvider(runner);

        Assert.Empty(await provider.SearchAsync("   ", 10));
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task NpmReadsItsGlobalListing()
    {
        var runner = new StubProcessRunner().RespondWith([.. Fixtures.ReadLines("npm-ls.json")]);
        var provider = new NpmProvider(runner);

        var package = Assert.Single(await provider.ListInstalledAsync());

        Assert.Equal(["ls", "--global", "--depth=0", "--json"], runner.LastRequest.Arguments);
        Assert.Equal("@kilocode/cli", package.Id);
        Assert.Equal("7.8.12", package.InstalledVersion);
        Assert.True(package.IsInstalled);
    }

    [Fact]
    public async Task NpmReportsNoUpdatesWhenTheOutdatedListingIsEmpty()
    {
        var runner = new StubProcessRunner().RespondWith([.. Fixtures.ReadLines("npm-outdated.json")]);
        var provider = new NpmProvider(runner);

        Assert.Empty(await provider.ListUpgradableAsync());
    }

    [Fact]
    public async Task NpmAcceptsExitCodeOneForTheOutdatedCheckBecauseItMeansUpdatesWereFound()
    {
        // npm exits 1 whenever `outdated` finds something, and prints the same JSON it would print
        // anyway. Treating that as a failure would hide every available update.
        const string json = """{"vue":{"current":"3.4.0","wanted":"3.5.0","latest":"3.5.44"}}""";
        var runner = new StubProcessRunner().Respond(StubProcessRunner.Exited(1, json));
        var provider = new NpmProvider(runner);

        var package = Assert.Single(await provider.ListUpgradableAsync());

        Assert.Equal("vue", package.Id);
        Assert.Equal("3.4.0", package.InstalledVersion);
        Assert.Equal("3.5.44", package.UpdateVersion);
    }

    [Fact]
    public async Task NpmStillReportsAFailureFromAnyOtherCommand()
    {
        var runner = new StubProcessRunner().Respond(StubProcessRunner.Exited(1, "npm ERR! network timeout"));
        var provider = new NpmProvider(runner);

        var error = await Assert.ThrowsAsync<PackageManagerException>(() => provider.ListInstalledAsync());

        Assert.Contains("npm ERR! network timeout", error.Message);
    }

    [Fact]
    public async Task NpmReadsPackageDetailsIncludingARepositoryObject()
    {
        var runner = new StubProcessRunner().RespondWith([.. Fixtures.ReadLines("npm-view.json")]);
        var provider = new NpmProvider(runner);

        var details = await provider.GetDetailsAsync(Package(NpmProvider.ProviderId, "vue"));

        Assert.NotNull(details);
        Assert.Equal("vue", details.Name);
        Assert.Equal("3.5.44", details.Version);
        Assert.Equal("https://vuejs.org/", details.Homepage);
        Assert.Equal("Evan You", details.Author);
        Assert.Equal("MIT", details.License);
        Assert.Equal(["vue"], details.Tags);
        Assert.Equal("git+https://github.com/vuejs/core.git", details.Fields["Repository"]);
    }

    [Fact]
    public async Task NpmToleratesFieldsThatTheRegistryDidNotReturn()
    {
        var runner = new StubProcessRunner().Respond(StubProcessRunner.Succeeded("""{"name":"tiny","version":"1.0.0"}"""));
        var provider = new NpmProvider(runner);

        var details = await provider.GetDetailsAsync(Package(NpmProvider.ProviderId, "tiny"));

        Assert.NotNull(details);
        Assert.Equal("tiny", details.Name);
        Assert.Equal("1.0.0", details.Version);
        Assert.Null(details.Homepage);
        Assert.Null(details.Author);
        Assert.Empty(details.Tags);
    }

    [Theory]
    [InlineData(PackageOperation.Install, null, "vue")]
    [InlineData(PackageOperation.Install, "3.5.0", "vue@3.5.0")]
    [InlineData(PackageOperation.Update, null, "vue@latest")]
    [InlineData(PackageOperation.Update, "3.5.0", "vue@3.5.0")]
    public async Task NpmQualifiesTheVersionOnTheSpecifier(PackageOperation operation, string? version, string expected)
    {
        var runner = new StubProcessRunner().Respond(StubProcessRunner.Silent());
        var provider = new NpmProvider(runner);

        await provider.ExecuteAsync(operation, Package(NpmProvider.ProviderId, "vue"), new InstallOptions { Version = version });

        // npm installs and updates with the same command; the specifier is what differs.
        Assert.Equal(["install", "--global", expected], runner.LastRequest.Arguments);
    }

    [Fact]
    public async Task NpmUsesTheUninstallCommandWithoutAVersion()
    {
        var runner = new StubProcessRunner().Respond(StubProcessRunner.Silent());
        var provider = new NpmProvider(runner);

        await provider.ExecuteAsync(PackageOperation.Uninstall, Package(NpmProvider.ProviderId, "vue"), InstallOptions.Default);

        Assert.Equal(["uninstall", "--global", "vue"], runner.LastRequest.Arguments);
    }

    // .NET tool ------------------------------------------------------------------------------

    [Fact]
    public async Task DotnetToolReadsItsSearchResults()
    {
        var runner = new StubProcessRunner().RespondWith([.. Fixtures.ReadLines("dotnet-tool-search.txt")]);
        var provider = new DotNetToolProvider(runner);

        var packages = await provider.SearchAsync("dotnet", 20);

        Assert.Equal(20, packages.Count);
        Assert.Equal(["tool", "search", "dotnet", "--take", "20"], runner.LastRequest.Arguments);

        var first = packages[0];
        Assert.Equal("dotnet-ef", first.Id);
        Assert.Equal("10.0.13", first.AvailableVersion);
        Assert.Equal(DotNetToolProvider.SourceName, first.Source);
    }

    [Fact]
    public async Task DotnetToolDoesNotSearchForAnEmptyTerm()
    {
        var runner = new StubProcessRunner();
        var provider = new DotNetToolProvider(runner);

        Assert.Empty(await provider.SearchAsync("  ", 10));
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task DotnetToolReadsItsInstalledListing()
    {
        var runner = new StubProcessRunner().RespondWith([.. Fixtures.ReadLines("dotnet-tool-list.txt")]);
        var provider = new DotNetToolProvider(runner);

        var package = Assert.Single(await provider.ListInstalledAsync());

        Assert.Equal(["tool", "list", "--global"], runner.LastRequest.Arguments);
        Assert.Equal("ilspycmd", package.Id);
        Assert.Equal("11.1.0.9782", package.InstalledVersion);
    }

    [Fact]
    public async Task DotnetToolFindsAnUpdateByLookingTheToolUp()
    {
        // The .NET CLI has no "list updates" command, so the provider lists what is installed and
        // then looks each tool up in the search index.
        string[] installed =
        [
            "Package Id      Version          Commands",
            "-----------------------------------------",
            "dotnet-ef       9.0.0            dotnet-ef",
        ];

        var runner = new StubProcessRunner()
            .RespondWith(installed)
            .RespondWith([.. Fixtures.ReadLines("dotnet-tool-search.txt")]);
        var provider = new DotNetToolProvider(runner);

        var package = Assert.Single(await provider.ListUpgradableAsync());

        Assert.Equal("dotnet-ef", package.Id);
        Assert.Equal("9.0.0", package.InstalledVersion);
        Assert.Equal("10.0.13", package.UpdateVersion);
        Assert.Equal(["tool", "search", "dotnet-ef", "--take", "5"], runner.LastRequest.Arguments);
    }

    [Fact]
    public async Task DotnetToolLeavesAToolAloneWhenTheRegistryCannotBeatIt()
    {
        string[] installed =
        [
            "Package Id      Version          Commands",
            "-----------------------------------------",
            "dotnet-ef       99.0.0           dotnet-ef",
        ];

        var runner = new StubProcessRunner()
            .RespondWith(installed)
            .RespondWith([.. Fixtures.ReadLines("dotnet-tool-search.txt")]);
        var provider = new DotNetToolProvider(runner);

        Assert.Empty(await provider.ListUpgradableAsync());
    }

    [Fact]
    public async Task DotnetToolReadsPackageDetailsFromTheSearchIndex()
    {
        var runner = new StubProcessRunner().RespondWith([.. Fixtures.ReadLines("dotnet-tool-search.txt")]);
        var provider = new DotNetToolProvider(runner);

        var details = await provider.GetDetailsAsync(Package(DotNetToolProvider.ProviderId, "dotnet-ef"));

        Assert.NotNull(details);
        Assert.Equal(["tool", "search", "dotnet-ef", "--take", "5"], runner.LastRequest.Arguments);
        Assert.Equal("dotnet-ef", details.Id);
        Assert.Equal("10.0.13", details.Version);
        Assert.Equal("Microsoft", details.Publisher);
        Assert.Equal("128764344", details.Fields["Downloads"]);
    }

    [Fact]
    public async Task DotnetToolPrefersAToolPathOverTheGlobalSwitch()
    {
        var (runner, provider) = Dotnet();

        await provider.ExecuteAsync(
            PackageOperation.Install,
            Package(DotNetToolProvider.ProviderId, "ilspycmd"),
            new InstallOptions { Location = @"C:\tools" });

        Assert.Equal(["tool", "install", "--tool-path", @"C:\tools", "ilspycmd"], runner.LastRequest.Arguments);
    }

    [Fact]
    public async Task DotnetToolPassesVersionAndSourceThrough()
    {
        var (runner, provider) = Dotnet();
        var options = new InstallOptions { Version = "1.2.3", Source = "https://api.nuget.org/v3/index.json" };

        await provider.ExecuteAsync(PackageOperation.Install, Package(DotNetToolProvider.ProviderId, "ilspycmd"), options);

        Assert.Equal(
            ["tool", "install", "--global", "ilspycmd", "--version", "1.2.3", "--add-source", "https://api.nuget.org/v3/index.json"],
            runner.LastRequest.Arguments);
    }

    [Fact]
    public async Task DotnetToolDoesNotForwardArgumentsWhenRemoving()
    {
        var (runner, provider) = Dotnet();

        await provider.ExecuteAsync(
            PackageOperation.Uninstall,
            Package(DotNetToolProvider.ProviderId, "ilspycmd"),
            new InstallOptions { CustomArguments = "--verbosity diag" });

        Assert.Equal(["tool", "uninstall", "--global", "ilspycmd"], runner.LastRequest.Arguments);
    }

    private static (StubProcessRunner Runner, DotNetToolProvider Provider) Dotnet()
    {
        var runner = new StubProcessRunner().Respond(StubProcessRunner.Silent());
        return (runner, new DotNetToolProvider(runner));
    }

    // PowerShell Gallery ---------------------------------------------------------------------

    [Fact]
    public async Task PowerShellReadsItsSearchResults()
    {
        var runner = new StubProcessRunner().RespondWith([.. Fixtures.ReadLines("powershell-find-module.json")]);
        var provider = new PowerShellGalleryProvider(runner);

        var packages = await provider.SearchAsync("git", 10);

        Assert.Equal(3, packages.Count);
        Assert.Equal("posh-git", packages[0].Id);
        Assert.Equal("1.1.0", packages[0].AvailableVersion);
        Assert.Equal(PowerShellGalleryProvider.SourceName, packages[0].Source);
        Assert.Contains("Find-Module -Name '*git*'", runner.LastRequest.Arguments[^1]);
    }

    [Fact]
    public async Task PowerShellReadsItsInstalledModules()
    {
        var runner = new StubProcessRunner().RespondWith([.. Fixtures.ReadLines("powershell-installed-modules.json")]);
        var provider = new PowerShellGalleryProvider(runner);

        Assert.Empty(await provider.ListInstalledAsync());
    }

    [Fact]
    public async Task PowerShellReadsModuleDetails()
    {
        var runner = new StubProcessRunner().RespondWith([.. Fixtures.ReadLines("powershell-module-details.json")]);
        var provider = new PowerShellGalleryProvider(runner);

        var details = await provider.GetDetailsAsync(Package(PowerShellGalleryProvider.ProviderId, "Pester"));

        Assert.NotNull(details);
        Assert.Equal("Pester", details.Name);
        Assert.Equal("6.2.0", details.Version);
        Assert.Equal("Pester Team", details.Author);
        Assert.Equal("nohwnd, fflaten", details.Publisher);
        Assert.Equal("https://github.com/Pester/Pester", details.Homepage);
        Assert.Equal("https://www.apache.org/licenses/LICENSE-2.0.html", details.LicenseUrl);
        Assert.Equal("2026-09-09", details.ReleaseDate);
        Assert.Contains("powershell", details.Tags);
        Assert.Contains("bdd", details.Tags);
        Assert.Equal("PSGallery", details.Fields["Repository"]);
    }

    [Fact]
    public async Task PowerShellComparesVersionsToDecideWhatIsUpgradable()
    {
        // The gallery has no "list updates" command either, so the script reports the installed
        // and available versions and the comparison happens here. A missing available version and
        // a version that is not newer must both be dropped.
        const string json =
            """[{"Name":"Pester","Installed":"6.1.0","Available":"6.2.0"},{"Name":"Older","Installed":"2.0","Available":"1.0"},{"Name":"Unlisted","Installed":"1.0","Available":null}]""";
        var runner = new StubProcessRunner().Respond(StubProcessRunner.Succeeded(json));
        var provider = new PowerShellGalleryProvider(runner);

        var package = Assert.Single(await provider.ListUpgradableAsync());

        Assert.Equal("Pester", package.Id);
        Assert.Equal("6.1.0", package.InstalledVersion);
        Assert.Equal("6.2.0", package.UpdateVersion);
    }

    [Fact]
    public async Task PowerShellQuotesAPackageIdSoItCannotBecomeScript()
    {
        var runner = new StubProcessRunner().Respond(StubProcessRunner.Succeeded("[]"));
        var provider = new PowerShellGalleryProvider(runner);
        var hostile = Package(PowerShellGalleryProvider.ProviderId, "a'; Remove-Item 'C:\\Temp' -Force");

        _ = await provider.GetDetailsAsync(hostile);

        // Each quote in the value is doubled, which is what keeps it inside the single-quoted
        // string instead of ending it and starting a new statement.
        Assert.Contains("Find-Module -Name 'a''; Remove-Item ''C:\\Temp'' -Force'", runner.LastRequest.Arguments[^1]);
    }

    [Fact]
    public async Task PowerShellStripsControlCharactersFromAValue()
    {
        var runner = new StubProcessRunner().Respond(StubProcessRunner.Succeeded("[]"));
        var provider = new PowerShellGalleryProvider(runner);
        var package = Package(PowerShellGalleryProvider.ProviderId, "line1\nline2");

        _ = await provider.GetDetailsAsync(package);

        var command = runner.LastRequest.Arguments[^1];
        Assert.Contains("-Name 'line1line2'", command);
        Assert.DoesNotContain("line1\nline2", command);
    }

    [Fact]
    public async Task PowerShellProbeMakesTheGalleryUsableAndReadsTheVersion()
    {
        var runner = new StubProcessRunner().Respond(StubProcessRunner.Succeeded("5.1.26100.9711"));
        var provider = new PowerShellGalleryProvider(runner);

        await provider.ProbeAsync();

        Assert.True(provider.IsAvailable);
        Assert.Equal("5.1.26100.9711", provider.Version);

        var arguments = runner.LastRequest.Arguments;
        Assert.Contains("-NoProfile", arguments);
        Assert.Contains("-NonInteractive", arguments);
        Assert.Contains("-ExecutionPolicy", arguments);
        Assert.Contains("Bypass", arguments);

        var command = arguments[^1];
        Assert.Contains("$ErrorActionPreference='Stop'", command);

        // PowerShellGet cannot reach the gallery until the NuGet provider is present, and it would
        // otherwise stop to ask permission. The probe installs it, once, for the current user.
        Assert.Contains("Install-PackageProvider -Name NuGet", command);
    }

    [Fact]
    public async Task PowerShellBuildsAScopedInstallScript()
    {
        var (runner, provider) = PowerShell();
        var options = new InstallOptions { Scope = "machine", Version = "6.2.0" };

        await provider.ExecuteAsync(PackageOperation.Install, Package(PowerShellGalleryProvider.ProviderId, "Pester"), options);

        Assert.Contains(
            "Install-Module -Name 'Pester' -Force -Scope AllUsers -AllowClobber -RequiredVersion '6.2.0'",
            runner.LastRequest.Arguments[^1]);
    }

    [Fact]
    public async Task PowerShellInstallsForTheCurrentUserByDefault()
    {
        var (runner, provider) = PowerShell();

        await provider.ExecuteAsync(PackageOperation.Install, Package(PowerShellGalleryProvider.ProviderId, "Pester"), InstallOptions.Default);

        Assert.Contains(
            "Install-Module -Name 'Pester' -Force -Scope CurrentUser -AllowClobber",
            runner.LastRequest.Arguments[^1]);
    }

    [Fact]
    public async Task PowerShellRemovesEveryVersionOfAModule()
    {
        var (runner, provider) = PowerShell();

        await provider.ExecuteAsync(PackageOperation.Uninstall, Package(PowerShellGalleryProvider.ProviderId, "Pester"), InstallOptions.Default);

        Assert.Contains(
            "Uninstall-Module -Name 'Pester' -Force -AllVersions",
            runner.LastRequest.Arguments[^1]);
    }

    private static (StubProcessRunner Runner, PowerShellGalleryProvider Provider) PowerShell()
    {
        var runner = new StubProcessRunner().Respond(StubProcessRunner.Silent());
        return (runner, new PowerShellGalleryProvider(runner));
    }
}
