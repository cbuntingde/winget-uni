using WingetUni.Providers.WinGet;

namespace WingetUni.Tests;

public sealed class WingetExitCodesTests
{
    [Theory]
    [InlineData(WingetExitCodes.NoApplicationsFound)]
    [InlineData(WingetExitCodes.RequiresAdmin)]
    [InlineData(WingetExitCodes.InstallerHashMismatch)]
    [InlineData(WingetExitCodes.NoNetwork)]
    [InlineData(WingetExitCodes.DiskFull)]
    public void ExplainsCodesThatChangeWhatTheUserShouldDo(int exitCode)
    {
        Assert.NotNull(WingetExitCodes.Describe(exitCode));
    }

    [Fact]
    public void ReturnsNothingForAnUnrecognizedCode()
    {
        Assert.Null(WingetExitCodes.Describe(0));
        Assert.Null(WingetExitCodes.Describe(1));
    }

    [Fact]
    public void RecognizesTheCodesThatAreKnownFromASuccessfulRun()
    {
        // Recorded from this machine: a search for a package that does not exist.
        Assert.Equal(-1978335212, WingetExitCodes.NoApplicationsFound);
        Assert.NotNull(WingetExitCodes.Describe(-1978335212));
    }

    [Fact]
    public void FlagsFailuresThatElevationOrARestartWouldResolve()
    {
        Assert.True(WingetExitCodes.NeedsElevation(WingetExitCodes.RequiresAdmin));
        Assert.False(WingetExitCodes.NeedsElevation(WingetExitCodes.NoApplicationsFound));

        Assert.True(WingetExitCodes.NeedsRestart(WingetExitCodes.RebootRequiredToFinish));
        Assert.True(WingetExitCodes.NeedsRestart(WingetExitCodes.RebootRequiredToInstall));
        Assert.False(WingetExitCodes.NeedsRestart(0));
    }

    [Fact]
    public void FormatsAnExitCodeAsTheHexWingetDocumentsIt()
    {
        // Winget documents its codes in hex, so the message must show 8A150019, not a
        // negative decimal.
        Assert.Equal("8A150019", WingetExitCodes.RequiresAdmin.ToString("X8"));
    }
}
