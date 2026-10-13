using System.Collections.Concurrent;
using Avala.Jobs.Contracts;
using Avala.Preview.Contracts;
using Avala.Preview.Pages;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Preview.Tests.Pages;

public sealed class GatewaysTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobsPageAndProxiesShareOneLeaseThatIsReleasedOnceTheJobClosesAsync()
    {
        using var worktree = new TemporaryFolder();
        var leases = new BlockLeases(Workloads.FreeBlock(BlockLeases.Size));
        await using var gateways = new Gateways(leases);
        var job = JobId.New();
        var server = new PreviewTarget(job, new Uri("http://localhost:5173/"), PreviewSource.Server);

        var files = Outcomes.Succeeds(await gateways.ServeFilesAsync(job, worktree.Path, Cancellation));
        var proxy = Outcomes.Succeeds(await gateways.ProxyAsync(server, Cancellation));

        Assert.Equal(leases.First, files);
        Assert.Equal(new Uri($"http://127.0.0.1:{leases.First + 1}/"), proxy);
        Assert.Equal(proxy, Outcomes.Succeeds(await gateways.ProxyAsync(server, Cancellation)));
        Assert.Equal(new HashSet<int> { leases.First, leases.First + 1 }, gateways.PortsOf(job));

        await gateways.CloseAsync(job, Cancellation);

        Assert.Empty(gateways.PortsOf(job));
        Assert.Equal([$"preview:{job.Value}"], leases.Released);
    }

    [Fact]
    public async Task StoppingAJobsPageFreesItsPortWhileTheJobKeepsItsLeaseAsync()
    {
        using var worktree = new TemporaryFolder();
        var leases = new BlockLeases(Workloads.FreeBlock(BlockLeases.Size));
        await using var gateways = new Gateways(leases);
        var job = JobId.New();
        var served = Outcomes.Succeeds(await gateways.ServeFilesAsync(job, worktree.Path, Cancellation));

        await gateways.StopFilesAsync(job, Cancellation);

        Assert.Empty(gateways.PortsOf(job));
        Assert.Empty(leases.Released);
        Assert.Equal(served, Outcomes.Succeeds(await gateways.ServeFilesAsync(job, worktree.Path, Cancellation)));
    }

    [Fact]
    public async Task AServedPageNeedsNoProxyAsync()
    {
        await using var gateways = new Gateways(new BlockLeases(Workloads.FreeBlock(BlockLeases.Size)));
        var page = new PreviewTarget(JobId.New(), new Uri("http://127.0.0.1:24010/index.html"), PreviewSource.File);

        Assert.Equal(page.Address, Outcomes.Succeeds(await gateways.ProxyAsync(page, Cancellation)));
    }

    [Fact]
    public async Task WithoutALeaseNoGatewayOpensAsync()
    {
        await using var gateways = new Gateways(new BlockLeases(0));
        var target = new PreviewTarget(JobId.New(), new Uri("http://localhost:5173/"), PreviewSource.Server);

        Assert.Equal(PreviewError.NoLease, Outcomes.FailsWith(await gateways.ProxyAsync(target, Cancellation)));
    }

    private sealed class BlockLeases(int first) : IPortLeases
    {
        public const int Size = 10;

        public int First { get; } = first;

        public ConcurrentQueue<string> Released { get; } = new();

        public ValueTask<Option<PortLease>> LeaseAsync(string holder, CancellationToken cancellationToken) =>
            ValueTask.FromResult(First == 0 ? Option<PortLease>.None : new PortLease(holder, First, First + Size - 1) { Own = true });

        public Task ReleaseAsync(string holder, CancellationToken cancellationToken)
        {
            Released.Enqueue(holder);

            return Task.CompletedTask;
        }
    }
}
