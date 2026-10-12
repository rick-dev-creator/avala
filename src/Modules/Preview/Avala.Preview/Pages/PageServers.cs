using System.Collections.Immutable;
using System.Globalization;
using Avala.Jobs.Contracts;
using Avala.Preview.Contracts;
using Avala.Preview.Running;
using Avala.Resources.Contracts;
using Avala.Sdk;

namespace Avala.Preview.Pages;

internal sealed class PageServers(IPortLeases leases) : IPageServers, IAsyncDisposable
{
    private readonly SerialExecutor owner = new();
    private ImmutableDictionary<JobId, PageServer> servers = ImmutableDictionary<JobId, PageServer>.Empty;

    public async ValueTask<Result<int, PreviewError>> StartAsync(JobId job, string worktree, CancellationToken cancellationToken) =>
        await owner.RunAsync(token => StartOwnedAsync(job, worktree, token), cancellationToken);

    public async ValueTask StopAsync(JobId job, CancellationToken cancellationToken) =>
        await owner.RunAsync(token => StopOwnedAsync(job, token), cancellationToken);

    public void AllowNetwork(JobId job, bool allowed)
    {
        if (Volatile.Read(ref servers).TryGetValue(job, out var server))
        {
            server.AllowsNetwork = allowed;
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var server in Volatile.Read(ref servers).Values)
        {
            await server.DisposeAsync();
        }

        await owner.DisposeAsync();
    }

    private static string Holder(JobId job) => string.Create(CultureInfo.InvariantCulture, $"preview:{job.Value}");

    private async Task<Result<int, PreviewError>> StartOwnedAsync(JobId job, string worktree, CancellationToken cancellationToken)
    {
        if (servers.TryGetValue(job, out var running))
        {
            return running.Port;
        }

        return await (await leases.LeaseAsync(Holder(job), cancellationToken)).Match(
            lease => ServeAsync(job, worktree, lease.First, cancellationToken),
            () => Task.FromResult(Result<int, PreviewError>.Failure(PreviewError.NoLease)));
    }

    private async Task<Result<int, PreviewError>> ServeAsync(JobId job, string worktree, int port, CancellationToken cancellationToken) =>
        await PageServer.Start(worktree, port).Match(
            server =>
            {
                Volatile.Write(ref servers, servers.SetItem(job, server));

                return Task.FromResult(Result<int, PreviewError>.Success(port));
            },
            async () =>
            {
                await leases.ReleaseAsync(Holder(job), cancellationToken);

                return Result<int, PreviewError>.Failure(PreviewError.NotStarted);
            });

    private async Task StopOwnedAsync(JobId job, CancellationToken cancellationToken)
    {
        if (!servers.TryGetValue(job, out var server))
        {
            return;
        }

        Volatile.Write(ref servers, servers.Remove(job));
        await server.DisposeAsync();
        await leases.ReleaseAsync(Holder(job), cancellationToken);
    }
}
