using Avala.Resources.Contracts;
using Avala.Resources.Leases;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;

namespace Avala.Resources.Tests.Leases;

public sealed class PortBookTests
{
    [Fact]
    public void EachWorktreeLeasesItsOwnFreeBlockAndKeepsItUntilTheRangeRunsOut()
    {
        var book = PortBook.Open(new PortRange(30_000, 30_029, 10));

        (book, var first) = book.Lease("/w/one", new HashSet<int>());
        (book, var again) = book.Lease("/w/one", new HashSet<int>());
        (book, var second) = book.Lease("/w/two", new HashSet<int> { 30_015 });
        (book, var none) = book.Lease("/w/three", new HashSet<int> { 30_015 });

        Assert.Equal(new PortLease("/w/one", 30_000, 30_009), Outcomes.Present(first));
        Assert.Equal(first, again);
        Assert.Equal(new PortLease("/w/two", 30_020, 30_029), Outcomes.Present(second));
        Assert.Equal(Option<PortLease>.None, none);
        Assert.Equal(2, book.Leases.Count);
    }

    [Fact]
    public void AReleasedBlockGoesToTheNextWorktree()
    {
        var (book, _) = PortBook.Open(new PortRange(30_000, 30_009, 10)).Lease("/w/one", new HashSet<int>());

        (book, var released) = book.Release("/w/one");
        (_, var next) = book.Lease("/w/two", new HashSet<int>());

        Assert.Equal(new PortLease("/w/one", 30_000, 30_009), Outcomes.Present(released));
        Assert.Equal(new PortLease("/w/two", 30_000, 30_009), Outcomes.Present(next));
        Assert.Equal(Option<PortLease>.None, book.Release("/w/unknown").Released);
    }

    [Fact]
    public void ALeasedPortHeldOutsideItsWorktreeIsAConflict()
    {
        var one = new PortLease("/w/one", 30_000, 30_009);
        var homes = new Dictionary<int, string> { [11] = "/w/one", [22] = "/w/two" };

        var conflicts = PortBook.Conflicts(
            [one],
            [(30_000, 11), (30_001, 22), (30_002, Option<int>.None), (40_000, 22)],
            homes);

        Assert.Equal([(30_001, one, Option<int>.Some(22)), (30_002, one, Option<int>.None)], conflicts);
    }

    [Fact]
    public void ALeaseReachesProcessesThroughTwoVariables() =>
        Assert.Equal(
            new Dictionary<string, string> { ["AVALA_PORT"] = "30000", ["AVALA_PORTS"] = "30000-30009" },
            PortBook.Variables(new PortLease("/w/one", 30_000, 30_009)));
}
