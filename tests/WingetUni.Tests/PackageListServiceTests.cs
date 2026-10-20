using WingetUni.Core.Models;
using WingetUni.Core.Services;

namespace WingetUni.Tests;

/// <summary>
/// Regression tests for the package list file: round-trip fidelity, rejection of files no
/// list could plausibly be, and atomic writes that leave no residue.
/// </summary>
public sealed class PackageListServiceTests
{
    private static string TemporaryPath() =>
        Path.Combine(Path.GetTempPath(), $"winget-uni-test-{Guid.NewGuid():N}.json");

    [Fact]
    public async Task RoundTripPreservesEntries()
    {
        var service = new PackageListService();
        var path = TemporaryPath();
        try
        {
            await service.SaveAsync(path, [new Package
            {
                ManagerId = "winget",
                Id = "Git.Git",
                Name = "Git",
                InstalledVersion = "2.55.0",
            }]);

            var file = await service.LoadAsync(path);

            Assert.Equal(PackageListService.CurrentFormatVersion, file.FormatVersion);
            var entry = Assert.Single(file.Packages);
            Assert.Equal("winget", entry.ManagerId);
            Assert.Equal("Git.Git", entry.Id);
            Assert.Equal("2.55.0", entry.Version);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AnOversizedFileIsRejected()
    {
        var service = new PackageListService();
        var path = TemporaryPath();
        try
        {
            await File.WriteAllBytesAsync(path, new byte[PackageListService.MaxFileBytes + 1]);

            await Assert.ThrowsAsync<InvalidDataException>(() => service.LoadAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SavingLeavesNoTemporaryFileBehind()
    {
        var service = new PackageListService();
        var path = TemporaryPath();
        try
        {
            await service.SaveAsync(path, []);

            Assert.False(File.Exists(path + ".tmp"));
            Assert.Empty((await service.LoadAsync(path)).Packages);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
