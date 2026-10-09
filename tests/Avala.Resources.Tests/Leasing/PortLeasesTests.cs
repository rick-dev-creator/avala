using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Resources.Tests.Leasing;

public sealed class PortLeasesTests
{
    private static CancellationToken Cancellation => Resourced.Cancellation;

    [Fact]
    public async Task EveryProcessTreeOfAWorktreeGetsThatWorktreesFreePortsInItsEnvironmentAsync()
    {
        await using var resources = new Resourced();
        resources.Listening.Listeners.Add(new Listener(24_004, Option<int>.None));
        var home = Resourced.Home("one");

        var first = await resources.Leases.ForAsync(home, Cancellation);
        var second = await resources.Leases.ForAsync(home + Path.DirectorySeparatorChar, Cancellation);

        Assert.Equal(new Dictionary<string, string> { ["AVALA_PORT"] = "24010", ["AVALA_PORTS"] = "24010-24019" }, first);
        Assert.Equal(first, second);
        Assert.Equal(new PortLease(home, 24_010, 24_019), Assert.Single(resources.Bus.Published.OfType<PortsLeased>()).Lease);
        Assert.Equal([new PortLease(home, 24_010, 24_019)], resources.Book.Leases());
    }

    [Fact]
    public async Task AWorktreeGetsNoPortsWhenTheRangeIsExhaustedAsync()
    {
        await using var resources = new Resourced(Avala.Resources.Settings.ResourceSettingsParser.Defaults with { Ports = new PortRange(24_000, 24_009, 10) });
        _ = await resources.Leases.ForAsync(Resourced.Home("one"), Cancellation);

        Assert.Empty(await resources.Leases.ForAsync(Resourced.Home("two"), Cancellation));
    }

    [Fact]
    public async Task TheLeaseOfAWorktreeIsReleasedWhenItsJobEndsAsync()
    {
        await using var resources = new Resourced();
        var job = JobId.New();
        var home = Resourced.Home("one");
        _ = await resources.OpenAsync(job, home);
        _ = await resources.Leases.ForAsync(home, Cancellation);

        await resources.Tracker.HandleAsync(new JobProgressed(job, JobStatus.AwaitingReview), Cancellation);
        Assert.Single(resources.Book.Leases());
        await resources.Tracker.HandleAsync(new JobProgressed(job, JobStatus.Discarded), Cancellation);

        Assert.Equal(new PortLease(home, 24_000, 24_009), Assert.Single(resources.Bus.Published.OfType<PortsReleased>()).Lease);
        Assert.Empty(resources.Book.Leases());
    }
}
