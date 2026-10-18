using WingetUni.Core.Models;
using WingetUni.Core.Services;

namespace WingetUni.Tests;

public sealed class SearchResultCacheTests
{
    [Fact]
    public void AnUnknownKeyIsAMiss()
    {
        var cache = new SearchResultCache();

        Assert.False(cache.TryGet("git", out _));
    }

    [Fact]
    public void AStoredOutcomeComesBack()
    {
        var cache = new SearchResultCache();
        var outcome = Outcome("git");

        cache.Set("key", outcome);

        Assert.True(cache.TryGet("key", out var found));
        Assert.Same(outcome, found);
    }

    [Fact]
    public void ClearForgetsEverything()
    {
        var cache = new SearchResultCache();
        cache.Set("key", Outcome("git"));

        cache.Clear();

        Assert.False(cache.TryGet("key", out _));
    }

    [Fact]
    public void WritingTheSameKeyTwiceReplacesTheValueWithoutFillingTheCache()
    {
        var cache = new SearchResultCache(capacity: 1);
        var first = Outcome("git");
        var second = Outcome("git-bash");

        cache.Set("key", first);
        cache.Set("key", second);

        Assert.True(cache.TryGet("key", out var found));
        Assert.Same(second, found);

        // A replacement must not count as a second entry, or this would evict the key it just
        // rewrote and the newer value would be lost.
        cache.Set("other", Outcome("node"));
        Assert.False(cache.TryGet("key", out _));
    }

    [Fact]
    public void TheOldestEntryIsDroppedOnceTheCacheIsFull()
    {
        var cache = new SearchResultCache(capacity: 2);
        cache.Set("a", Outcome("git"));
        cache.Set("b", Outcome("node"));
        cache.Set("c", Outcome("python"));

        Assert.False(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
    }

    [Fact]
    public void ACapacityBelowOneIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SearchResultCache(capacity: 0));
    }

    [Fact]
    public void TheKeyIgnoresTheOrderOfTheManagers()
    {
        var first = SearchResultCache.Key("git", 25, ["winget", "choco"]);
        var second = SearchResultCache.Key("git", 25, ["choco", "winget"]);

        Assert.Equal(first, second);
    }

    [Fact]
    public void TheKeySeparatesDifferentSearches()
    {
        var baseline = SearchResultCache.Key("git", 25, ["winget"]);

        Assert.NotEqual(baseline, SearchResultCache.Key("node", 25, ["winget"]));
        Assert.NotEqual(baseline, SearchResultCache.Key("git", 50, ["winget"]));
        Assert.NotEqual(baseline, SearchResultCache.Key("git", 25, ["winget", "choco"]));

        // A term containing the separator must not collide with one that does not.
        Assert.NotEqual(
            SearchResultCache.Key("git,winget", 25, []),
            SearchResultCache.Key("git", 25, ["winget"]));
    }

    [Fact]
    public void NoManagersStillProducesAKey()
    {
        Assert.Equal(
            SearchResultCache.Key("git", 25, []),
            SearchResultCache.Key("git", 25, []));
    }

    [Fact]
    public void MissingArgumentsAreRejected()
    {
        var cache = new SearchResultCache();

        Assert.Throws<ArgumentNullException>(() => SearchResultCache.Key(null!, 25, ["winget"]));
        Assert.Throws<ArgumentNullException>(() => SearchResultCache.Key("git", 25, null!));
        Assert.Throws<ArgumentNullException>(() => cache.TryGet(null!, out _));
        Assert.Throws<ArgumentNullException>(() => cache.Set(null!, Outcome("git")));
        Assert.Throws<ArgumentNullException>(() => cache.Set("key", null!));
    }

    private static PackageQueryOutcome Outcome(string name) => new(
        [
            new Package
            {
                ManagerId = "winget",
                Id = name,
                Name = name,
                AvailableVersion = "1.0.0",
            },
        ],
        []);
}
