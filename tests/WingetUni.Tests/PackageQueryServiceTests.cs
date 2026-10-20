using WingetUni.Core.Abstractions;
using WingetUni.Core.Models;
using WingetUni.Core.Services;
using WingetUni.Providers.DotNetTool;
using WingetUni.Providers.Pip;

namespace WingetUni.Tests;

/// <summary>
/// Exercises the aggregation contract: one manager failing must not hide the others,
/// cancellation must propagate, and managers without a search command must not attach
/// an error to every multi-manager search.
/// </summary>
public sealed class PackageQueryServiceTests
{
    private static Package Package(string managerId, string id) => new()
    {
        ManagerId = managerId,
        Id = id,
        Name = id,
    };

    [Fact]
    public async Task APassingManagerSurvivesAFailingOne()
    {
        var good = new FakeProvider("good");
        good.OnSearch = (_, _, _) => Task.FromResult<IReadOnlyList<Package>>([Package("good", "a")]);
        var bad = new FakeProvider("bad");
        bad.OnSearch = (_, _, _) => throw new InvalidOperationException("boom");
        var service = new PackageQueryService(new PackageManagerRegistry([good, bad]));

        var outcome = await service.SearchAsync("a", 10);

        Assert.Equal("a", Assert.Single(outcome.Packages).Id);
        Assert.Contains("bad", Assert.Single(outcome.Errors));
    }

    [Fact]
    public async Task CancellationPropagatesInsteadOfBecomingAnError()
    {
        var provider = new FakeProvider("good");
        provider.OnSearch = (_, _, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<Package>>([]);
        };
        var service = new PackageQueryService(new PackageManagerRegistry([provider]));

        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.SearchAsync("a", 10, cancellationToken: canceled.Token));
    }

    [Fact]
    public async Task AMultiManagerSearchSkipsManagersThatCannotSearch()
    {
        var searched = false;
        var pipLike = new FakeProvider(PipProvider.ProviderId, supportsSearch: false);
        pipLike.OnSearch = (_, _, _) =>
        {
            searched = true;
            throw new PackageManagerException("pip has no search command");
        };
        var good = new FakeProvider("good");
        good.OnSearch = (_, _, _) => Task.FromResult<IReadOnlyList<Package>>([Package("good", "a")]);
        var service = new PackageQueryService(new PackageManagerRegistry([pipLike, good]));

        var outcome = await service.SearchAsync("a", 10);

        Assert.Equal("a", Assert.Single(outcome.Packages).Id);
        Assert.Empty(outcome.Errors);
        Assert.False(searched);
    }

    [Fact]
    public async Task SearchingOnlyAManagerThatCannotSearchExplainsWhy()
    {
        var pipLike = new FakeProvider(PipProvider.ProviderId, supportsSearch: false);
        pipLike.OnSearch = (_, _, _) => throw new PackageManagerException("pip has no search command");
        var service = new PackageQueryService(new PackageManagerRegistry([pipLike]));

        var outcome = await service.SearchAsync("a", 10, [PipProvider.ProviderId]);

        Assert.Empty(outcome.Packages);
        Assert.Contains("no search command", Assert.Single(outcome.Errors));
    }

    [Fact]
    public void SearchCapabilityMatchesTheTools()
    {
        var runner = new StubProcessRunner();

        Assert.False(new PipProvider(runner).SupportsSearch);
        Assert.True(new DotNetToolProvider(runner).SupportsSearch);
    }

    private sealed class FakeProvider : IPackageManagerProvider
    {
        public FakeProvider(string id, bool supportsSearch = true)
        {
            Id = id;
            DisplayName = id;
            SupportsSearch = supportsSearch;
        }

        public string Id { get; }

        public string DisplayName { get; }

        public bool IsAvailable { get; set; } = true;

        public string? Version => "1.0";

        public string? UnavailableReason => null;

        public bool SupportsSearch { get; }

        public Func<string, int, CancellationToken, Task<IReadOnlyList<Package>>>? OnSearch { get; set; }

        public Task ProbeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<Package>> SearchAsync(
            string query,
            int limit,
            CancellationToken cancellationToken = default) =>
            OnSearch is null
                ? Task.FromResult<IReadOnlyList<Package>>([])
                : OnSearch(query, limit, cancellationToken);

        public Task<IReadOnlyList<Package>> ListInstalledAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Package>>([]);

        public Task<IReadOnlyList<Package>> ListUpgradableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Package>>([]);

        public Task<PackageDetails?> GetDetailsAsync(
            Package package,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PackageDetails?>(null);

        public Task<OperationResult> ExecuteAsync(
            PackageOperation operation,
            Package package,
            InstallOptions options,
            IProgress<OperationProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
