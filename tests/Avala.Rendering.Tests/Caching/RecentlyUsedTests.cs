using Avala.Rendering.Caching;

namespace Avala.Rendering.Tests.Caching;

public sealed class RecentlyUsedTests
{
    private readonly List<string> evicted = [];

    [Fact]
    public void KeepingBeyondTheBudgetEvictsTheLeastRecentlyUsedDrawings()
    {
        var cache = Cache(budget: 20);
        cache.Keep("a", "a");
        cache.Keep("b", "b");
        _ = cache.Find("a");

        cache.Keep("c", "c");
        cache.Keep("d", "d");

        Assert.Equal(["b", "a"], evicted);
        Assert.Equal(["d", "c"], cache.Kept);
        Assert.True(cache.Find("a").IsNone);
    }

    [Fact]
    public void TheNewestDrawingIsKeptEvenWhenItAloneIsOverTheBudget()
    {
        var cache = Cache(budget: 5);

        cache.Keep("a", "a");

        Assert.Equal(["a"], cache.Kept);
        Assert.Empty(evicted);
    }

    private RecentlyUsed<string> Cache(long budget) => new(budget, _ => 10, evicted.Add);
}
