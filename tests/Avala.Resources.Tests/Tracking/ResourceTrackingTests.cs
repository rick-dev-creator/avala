using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Resources.Usage;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;

namespace Avala.Resources.Tests.Tracking;

public sealed class ResourceTrackingTests
{
    [Fact]
    public async Task UsageIsAttributedToItsJobSessionConnectionAndProviderAndCountedGloballyAsync()
    {
        await using var resources = new Resourced();
        var build = JobId.New();
        var review = JobId.New();
        var (session, _) = await resources.OpenAsync(build, Resourced.Home("one"), "work", "simulator", Process(11, 100), Process(12, 50));
        _ = await resources.OpenAsync(review, Resourced.Home("two"), "personal", "other", Process(21, 30));
        resources.Listening.Listeners.Add(new Listener(24_000, 11));
        resources.Folders.Sizes[Resourced.Home("one")] = 4_000;
        resources.Folders.Data = 9_000;

        var sample = await resources.SampleAsync();

        Assert.Equal(Outcomes.Present(resources.Book.Latest), sample);
        Assert.Equal("2 processes, 150 bytes, 00:00:02 cpu, load 0, ports 24000, disk 4000", Described(resources.Book.OfJob(build)));
        Assert.Equal(Described(resources.Book.OfJob(build)), Described(resources.Book.OfSession(session)));
        Assert.Equal("3 processes, 180 bytes, 00:00:03 cpu, load 0, ports 24000, disk 9000", Described(resources.Book.Global()));
        Assert.Equal(
            [("personal", 1), ("work", 2)],
            resources.Book.ByConnection().Select(used => (used.Connection.Value, used.Usage.Processes)));
        Assert.Equal(
            [("other", 30L), ("simulator", 150L)],
            resources.Book.ByProvider().Select(used => (used.Provider, used.Usage.MemoryBytes)));
        Assert.Equal(Option<JobId>.Some(build), Assert.Single(sample.Worktrees, folder => folder.Bytes == 4_000).Job);
    }

    [Fact]
    public async Task WorktreesAreMeasuredOnlyWhenTheirDiskIsDueAndKeepTheirLastMeasureAsync()
    {
        await using var resources = new Resourced();
        _ = await resources.OpenAsync(JobId.New(), Resourced.Home("one"));
        resources.Folders.Sizes[Resourced.Home("one")] = 4_000;

        var measured = await resources.SampleAsync(disks: true);
        resources.Folders.Sizes[Resourced.Home("one")] = 8_000;
        var reused = await resources.SampleAsync(disks: false);

        Assert.Equal(1, resources.Folders.Measured);
        Assert.Equal(measured.Worktrees, reused.Worktrees);
    }

    [Fact]
    public void CpuLoadIsTheCpuTimeUsedBetweenTwoSamplesAndANewProcessCountsFromItsNextSample()
    {
        var before = new Dictionary<int, TimeSpan> { [11] = TimeSpan.FromSeconds(10) };
        var now = new[]
        {
            new ProcessUsage(11, "dotnet", 0, TimeSpan.FromSeconds(13), []),
            new ProcessUsage(12, "dotnet", 0, TimeSpan.FromSeconds(50), []),
        };

        Assert.Equal(1.5, Tallies.Load(before, now, TimeSpan.FromSeconds(2)));
        Assert.Equal(0, Tallies.Load(before, now, TimeSpan.Zero));
    }

    [Fact]
    public async Task ALeasedPortHeldOutsideItsWorktreeIsObservedOnceAsync()
    {
        await using var resources = new Resourced();
        var home = Resourced.Home("one");
        _ = await resources.OpenAsync(JobId.New(), home);
        _ = await resources.Leases.ForAsync(home, Resourced.Cancellation);
        resources.Listening.Listeners.Add(new Listener(24_003, Option<int>.None));

        _ = await resources.SampleAsync();
        _ = await resources.SampleAsync();

        var conflict = Assert.Single(resources.Bus.Published.OfType<PortConflictObserved>()).Conflict;
        Assert.Equal((24_003, Path.TrimEndingDirectorySeparator(Path.GetFullPath(home)), Option<int>.None), (conflict.Port, conflict.Lease.Worktree, conflict.Process));
        Assert.Equal([conflict], resources.Book.Conflicts());
    }

    [Fact]
    public async Task WithoutASampleEveryQueryAnswersNothingAsync()
    {
        await using var resources = new Resourced();
        _ = await resources.OpenAsync(JobId.New(), Resourced.Home("one"));

        Assert.Equal(Tallies.Nothing, resources.Book.Global());
        Assert.Equal(Tallies.Nothing, resources.Book.OfJob(JobId.New()));
        Assert.Equal([new ConnectionResources(new ConnectionName("work"), Tallies.Nothing)], resources.Book.ByConnection());
    }

    private static TreeProcess Process(int id, long memory) => new(id, "dotnet", memory, TimeSpan.FromSeconds(1));

    private static string Described(ResourceUsage usage) =>
        System.FormattableString.Invariant(
            $"{usage.Processes} processes, {usage.MemoryBytes} bytes, {usage.CpuTime} cpu, load {usage.CpuLoad}, ports {string.Join(',', usage.Ports)}, disk {usage.DiskBytes}");
}
