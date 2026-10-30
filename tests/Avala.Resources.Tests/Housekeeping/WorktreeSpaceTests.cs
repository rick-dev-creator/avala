using Avala.Resources.Contracts;
using Avala.Sdk;

namespace Avala.Resources.Tests.Housekeeping;

public sealed class WorktreeSpaceTests
{
    [Theory]
    [InlineData(5L, null, null, null, true)]
    [InlineData(4L, 4L, null, null, true)]
    [InlineData(5L, 4L, null, null, false)]
    [InlineData(5L, null, null, 0.1, true)]
    [InlineData(5L, null, 10L, 0.1, true)]
    [InlineData(5L, null, 5L, 0.1, false)]
    public void WorktreesAreWithinTheirSpaceUnlessOverBudgetOrTheDiskIsBelowItsFloor(long worktrees, long? budget, long? available, double? floor, bool within)
    {
        var space = new WorktreeSpace(worktrees, budget.ToOption(), available.ToOption().Map(free => new DiskSpace(free, 100)), floor.ToOption(), DateTimeOffset.UnixEpoch);

        Assert.Equal(within, space.Within);
    }
}
