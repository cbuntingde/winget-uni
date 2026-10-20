using WingetUni.Core.Models;
using WingetUni.Providers.DotNetTool;
using WingetUni.Providers.Npm;
using WingetUni.Providers.Pip;
using WingetUni.Providers.PowerShellGallery;
using WingetUni.Providers.Scoop;

namespace WingetUni.Tests;

/// <summary>
/// Regression tests for the provider robustness fixes: isolated per-tool failures, blank-id
/// rows that must never become actionable, and argument building that honours only what the
/// tool supports.
/// </summary>
public sealed class ProviderResilienceTests
{
    private static Package Package(string managerId, string id) => new()
    {
        ManagerId = managerId,
        Id = id,
        Name = id,
    };

    [Fact]
    public async Task DotnetToolSkipsAToolWhoseLookupFails()
    {
        string[] installed =
        [
            "Package Id      Version          Commands",
            "-----------------------------------------",
            "tool-a          1.0.0            tool-a",
            "tool-b          1.0.0            tool-b",
        ];
        string[] search =
        [
            "Package ID      Latest Version      Authors     Downloads",
            "----------------------------------------------------------",
            "tool-b          2.0.0               Someone     42",
        ];

        var runner = new StubProcessRunner()
            .RespondWith(installed)
            .Respond(StubProcessRunner.Exited(1, "registry error"))
            .RespondWith(search);
        var provider = new DotNetToolProvider(runner);

        var package = Assert.Single(await provider.ListUpgradableAsync());

        Assert.Equal("tool-b", package.Id);
        Assert.Equal("2.0.0", package.UpdateVersion);
    }

    [Fact]
    public async Task NpmSkipsSearchRowsWithoutAName()
    {
        var runner = new StubProcessRunner()
            .RespondWith("""[{"name": "express", "version": "5.0.0"}, {"version": "1.0.0"}]""");
        var provider = new NpmProvider(runner);

        var package = Assert.Single(await provider.SearchAsync("express", 10));

        Assert.Equal("express", package.Id);
    }

    [Fact]
    public async Task PowerShellGallerySkipsRowsWithoutAName()
    {
        const string Payload =
            """[{"Name": "Pester", "Version": "5.0.0", "Repository": "PSGallery"}, {"Version": "9.9.9"}]""";

        var search = new StubProcessRunner().RespondWith(Payload);
        var found = await new PowerShellGalleryProvider(search).SearchAsync("pester", 10);
        Assert.Equal("Pester", Assert.Single(found).Id);

        var installedRunner = new StubProcessRunner().RespondWith(Payload);
        var installed = await new PowerShellGalleryProvider(installedRunner).ListInstalledAsync();
        Assert.Equal("Pester", Assert.Single(installed).Id);
    }

    [Fact]
    public async Task PipSkipsListingsWithoutAName()
    {
        var runner = new StubProcessRunner()
            .RespondWith("""[{"name": "pip", "version": "26.0"}, {"version": "1.0"}]""");
        var provider = new PipProvider(runner);

        var package = Assert.Single(await provider.ListInstalledAsync());

        Assert.Equal("pip", package.Id);
    }

    [Fact]
    public async Task ScoopPinsAVersionOnlyWhenInstalling()
    {
        var options = new InstallOptions { Version = "1.2.3" };

        var install = new StubProcessRunner().RespondWith();
        await new ScoopProvider(install).ExecuteAsync(
            PackageOperation.Install, Package(ScoopProvider.ProviderId, "git"), options);
        Assert.Equal(["install", "git@1.2.3"], install.LastRequest.Arguments);

        var update = new StubProcessRunner().RespondWith();
        await new ScoopProvider(update).ExecuteAsync(
            PackageOperation.Update, Package(ScoopProvider.ProviderId, "git"), options);
        Assert.Equal(["update", "git"], update.LastRequest.Arguments);

        var remove = new StubProcessRunner().RespondWith();
        await new ScoopProvider(remove).ExecuteAsync(
            PackageOperation.Uninstall, Package(ScoopProvider.ProviderId, "git"), options);
        Assert.Equal(["uninstall", "git"], remove.LastRequest.Arguments);
    }

    [Fact]
    public async Task ScoopIgnoresOptionsTheToolDoesNotSupport()
    {
        var options = new InstallOptions
        {
            Location = """C:\Tools""",
            CustomArguments = "--silent",
        };

        var runner = new StubProcessRunner().RespondWith();
        await new ScoopProvider(runner).ExecuteAsync(
            PackageOperation.Install, Package(ScoopProvider.ProviderId, "git"), options);

        Assert.Equal(["install", "git"], runner.LastRequest.Arguments);
    }

    [Fact]
    public async Task ScoopStillHonoursAMachineScope()
    {
        var runner = new StubProcessRunner().RespondWith();
        await new ScoopProvider(runner).ExecuteAsync(
            PackageOperation.Install,
            Package(ScoopProvider.ProviderId, "git"),
            new InstallOptions { Scope = "machine" });

        Assert.Equal(["install", "git", "--global"], runner.LastRequest.Arguments);
    }
}
