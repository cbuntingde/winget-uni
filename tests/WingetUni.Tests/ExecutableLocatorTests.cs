using WingetUni.Providers;

namespace WingetUni.Tests;

public sealed class ExecutableLocatorTests
{
    [Fact]
    public void ResolvesABareNameThroughThePathToAFullPath()
    {
        var resolved = ExecutableLocator.Resolve("cmd");

        // The point of the locator is that a batch shim started by its bare name cannot find its
        // own folder, so a resolved result has to be an absolute path to the real file.
        Assert.True(Path.IsPathRooted(resolved), $"'{resolved}' is not rooted.");
        Assert.Equal("cmd.exe", Path.GetFileName(resolved)!, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolvesANameThatAlreadyCarriesAnExtension()
    {
        var resolved = ExecutableLocator.Resolve("cmd.exe");

        Assert.True(Path.IsPathRooted(resolved));
        Assert.Equal("cmd.exe", Path.GetFileName(resolved)!, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void LeavesANameWithADirectoryAlone()
    {
        // An absolute or relative path is deliberate, so it is not searched for.
        const string relative = @"tools\scoop.cmd";
        const string absolute = @"C:\tools\scoop.cmd";

        Assert.Equal(relative, ExecutableLocator.Resolve(relative));
        Assert.Equal(absolute, ExecutableLocator.Resolve(absolute));
    }

    [Fact]
    public void ReturnsAnUnknownNameUnchanged()
    {
        // The process runner turns the unchanged name into the usual "could not be started"
        // reason, which is how a missing manager is reported.
        Assert.Equal("no-such-tool-9f3a", ExecutableLocator.Resolve("no-such-tool-9f3a"));
        Assert.Equal("no-such-tool-9f3a.exe", ExecutableLocator.Resolve("no-such-tool-9f3a.exe"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ReturnsABlankNameUnchanged(string name)
    {
        Assert.Equal(name, ExecutableLocator.Resolve(name));
    }

    [Fact]
    public void FindsTheDotnetHostThatIsRunningTheseTests()
    {
        // dotnet has to be on the PATH for the tests to be running at all, so this is a real
        // resolution rather than a guess about the machine.
        var resolved = ExecutableLocator.Resolve("dotnet");

        Assert.True(Path.IsPathRooted(resolved));
        Assert.Equal("dotnet", Path.GetFileNameWithoutExtension(resolved)!, StringComparer.OrdinalIgnoreCase);
    }
}
