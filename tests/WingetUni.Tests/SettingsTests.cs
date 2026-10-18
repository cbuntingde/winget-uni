using WingetUni.Core.Services;

namespace WingetUni.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "wingetuni-settings-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A temporary directory left behind is not a test failure.
        }
    }

    [Fact]
    public void ANewInstallSearchesOneManagerAndAsksForFewResults()
    {
        var settings = new AppSettings();

        // Never chosen, so the primary manager answers rather than every manager on the machine.
        Assert.Null(settings.DiscoverManagerId);
        Assert.Equal(25, settings.SearchResultLimit);
    }

    [Fact]
    public void AnUnchosenManagerFallsBackToTheOneItWasGiven()
    {
        Assert.Equal("winget", new AppSettings().ResolveDiscoverManagerId("winget"));
    }

    [Fact]
    public void AnUnchosenManagerWithNothingToFallBackOnMeansEveryManager()
    {
        Assert.Null(new AppSettings().ResolveDiscoverManagerId(null));
    }

    [Fact]
    public void AChosenManagerIsUsedAsItIs()
    {
        Assert.Equal("chocolatey", new AppSettings { DiscoverManagerId = "chocolatey" }.ResolveDiscoverManagerId("winget"));
    }

    [Fact]
    public void AllManagersMeansNoParticularOne()
    {
        var settings = new AppSettings { DiscoverManagerId = AppSettings.AllManagers };

        Assert.Null(settings.ResolveDiscoverManagerId("winget"));
    }

    [Fact]
    public async Task TheChosenManagerSurvivesASaveAndLoad()
    {
        await new SettingsService(_directory).SaveAsync(new AppSettings { DiscoverManagerId = "chocolatey" });

        var reloaded = new SettingsService(_directory);
        await reloaded.LoadAsync();

        Assert.Null(reloaded.LastLoadError);
        Assert.Equal("chocolatey", reloaded.Current.DiscoverManagerId);
    }

    [Fact]
    public async Task SearchingAllManagersSurvivesASaveAndLoad()
    {
        await new SettingsService(_directory).SaveAsync(new AppSettings { DiscoverManagerId = AppSettings.AllManagers });

        var reloaded = new SettingsService(_directory);
        await reloaded.LoadAsync();

        // The sentinel has to come back as itself. Were it written as an empty string it would
        // read as "never chosen" and silently hand the user back a single manager.
        Assert.Equal(AppSettings.AllManagers, reloaded.Current.DiscoverManagerId);
        Assert.NotEqual(string.Empty, reloaded.Current.DiscoverManagerId);
    }

    [Fact]
    public async Task SwitchedOffManagersSurviveASaveAndLoad()
    {
        await new SettingsService(_directory).SaveAsync(new AppSettings().WithManagerEnabled("scoop", false));

        var reloaded = new SettingsService(_directory);
        await reloaded.LoadAsync();

        Assert.False(reloaded.Current.IsManagerEnabled("scoop"));
        Assert.True(reloaded.Current.IsManagerEnabled("winget"));
    }

    [Fact]
    public void ANewInstallHidesInTheNotificationAreaWhenClosed()
    {
        Assert.True(new AppSettings().MinimizeToTrayOnClose);
    }

    [Fact]
    public async Task ChoosingToExitOnCloseSurvivesASaveAndLoad()
    {
        await new SettingsService(_directory).SaveAsync(new AppSettings { MinimizeToTrayOnClose = false });

        var reloaded = new SettingsService(_directory);
        await reloaded.LoadAsync();

        Assert.False(reloaded.Current.MinimizeToTrayOnClose);
    }

    [Fact]
    public async Task AnUnreadableFileFallsBackToDefaultsAndSaysWhy()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "settings.json"), "{ not json");

        var service = new SettingsService(_directory);
        await service.LoadAsync();

        Assert.Equal(new AppSettings(), service.Current);
        Assert.NotNull(service.LastLoadError);
    }

    [Fact]
    public async Task AMissingFileIsNotReportedAsAProblem()
    {
        var service = new SettingsService(_directory);

        await service.LoadAsync();

        Assert.Null(service.LastLoadError);
        Assert.Equal(new AppSettings(), service.Current);
    }
}
