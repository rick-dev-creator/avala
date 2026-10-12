using System.Collections.Immutable;
using System.Globalization;
using Avala.Jobs.Contracts;
using Avala.Preview.Contracts;
using Avala.Preview.Running;
using Avala.Resources.Contracts;
using Avala.Sdk;

namespace Avala.Preview.Pages;

internal sealed record JobGateways(PortLease Lease, Option<PageServer> Files, ImmutableDictionary<int, PageProxy> Proxies, bool Network)
{
    public ImmutableHashSet<int> Ports =>
        [.. Files.Match<int[]>(files => [files.Port], () => []), .. Proxies.Values.Select(proxy => proxy.Port)];

    public Option<int> FreePort =>
        Enumerable.Range(Lease.First + 1, Math.Max(0, Lease.Last - Lease.First))
            .Where(port => !Proxies.Values.Any(proxy => proxy.Port == port))
            .Select(Option<int>.Some)
            .FirstOrDefault();
}

internal sealed class Gateways(IPortLeases leases) : IGateways, IAsyncDisposable
{
    private readonly SerialExecutor owner = new();
    private ImmutableDictionary<JobId, JobGateways> jobs = ImmutableDictionary<JobId, JobGateways>.Empty;

    public async ValueTask<Result<int, PreviewError>> ServeFilesAsync(JobId job, string worktree, CancellationToken cancellationToken) =>
        await owner.RunAsync(token => ServeFilesOwnedAsync(job, worktree, token), cancellationToken);

    public async ValueTask StopFilesAsync(JobId job, CancellationToken cancellationToken) =>
        await owner.RunAsync(_ => StopFilesOwnedAsync(job), cancellationToken);

    public async ValueTask<Result<Uri, PreviewError>> ProxyAsync(PreviewTarget target, CancellationToken cancellationToken) =>
        target.Source == PreviewSource.File
            ? target.Address
            : await owner.RunAsync(token => ProxyOwnedAsync(target, token), cancellationToken);

    public async ValueTask CloseAsync(JobId job, CancellationToken cancellationToken) =>
        await owner.RunAsync(token => CloseOwnedAsync(job, token), cancellationToken);

    public IReadOnlySet<int> PortsOf(JobId job) => Volatile.Read(ref jobs).TryGetValue(job, out var gateways) ? gateways.Ports : [];

    public async ValueTask AllowNetworkAsync(JobId job, bool allowed, CancellationToken cancellationToken) =>
        await owner.RunAsync(_ => Task.FromResult(AllowNetworkOwned(job, allowed)), cancellationToken);

    public async ValueTask DisposeAsync()
    {
        foreach (var job in Volatile.Read(ref jobs).Keys)
        {
            await CloseAsync(job, CancellationToken.None);
        }

        await owner.DisposeAsync();
    }

    private static string Holder(JobId job) => string.Create(CultureInfo.InvariantCulture, $"preview:{job.Value}");

    private static Uri Address(int port) => new(string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{port}/"));

    private void Keep(JobId job, JobGateways gateways) => Volatile.Write(ref jobs, jobs.SetItem(job, gateways));

    private async Task<Option<JobGateways>> OpenAsync(JobId job, CancellationToken cancellationToken) =>
        jobs.TryGetValue(job, out var known)
            ? known
            : (await leases.LeaseAsync(Holder(job), cancellationToken)).Map(lease => new JobGateways(lease, Option<PageServer>.None, ImmutableDictionary<int, PageProxy>.Empty, Network: false));

    private async Task<Result<int, PreviewError>> ServeFilesOwnedAsync(JobId job, string worktree, CancellationToken cancellationToken) =>
        (await OpenAsync(job, cancellationToken)).Match(
            gateways => ServeFiles(job, worktree, gateways),
            () => Result<int, PreviewError>.Failure(PreviewError.NoLease));

    private Result<int, PreviewError> ServeFiles(JobId job, string worktree, JobGateways gateways) =>
        gateways.Files.Match(
            running => Result<int, PreviewError>.Success(running.Port),
            () => PageServer.Start(worktree, gateways.Lease.First).Match(
                files =>
                {
                    files.AllowsNetwork = gateways.Network;
                    Keep(job, gateways with { Files = files });

                    return Result<int, PreviewError>.Success(files.Port);
                },
                () => Result<int, PreviewError>.Failure(PreviewError.NotStarted)));

    private async Task<bool> StopFilesOwnedAsync(JobId job)
    {
        if (!jobs.TryGetValue(job, out var gateways))
        {
            return false;
        }

        Keep(job, gateways with { Files = Option<PageServer>.None });
        await gateways.Files.Match(files => files.DisposeAsync().AsTask(), () => Task.CompletedTask);

        return true;
    }

    private async Task<Result<Uri, PreviewError>> ProxyOwnedAsync(PreviewTarget target, CancellationToken cancellationToken) =>
        (await OpenAsync(target.Job, cancellationToken)).Match(
            gateways => Proxy(target, gateways),
            () => Result<Uri, PreviewError>.Failure(PreviewError.NoLease));

    private Result<Uri, PreviewError> Proxy(PreviewTarget target, JobGateways gateways) =>
        gateways.Proxies.TryGetValue(target.Address.Port, out var known)
            ? Address(known.Port)
            : gateways.FreePort.Bind(port => PageProxy.Start(port, target.Address.Port)).Match(
                proxy =>
                {
                    proxy.AllowsNetwork = gateways.Network;
                    Keep(target.Job, gateways with { Proxies = gateways.Proxies.SetItem(target.Address.Port, proxy) });

                    return Result<Uri, PreviewError>.Success(Address(proxy.Port));
                },
                () => Result<Uri, PreviewError>.Failure(PreviewError.NoLease));

    private async Task<bool> CloseOwnedAsync(JobId job, CancellationToken cancellationToken)
    {
        if (!jobs.TryGetValue(job, out var gateways))
        {
            return false;
        }

        Volatile.Write(ref jobs, jobs.Remove(job));

        await gateways.Files.Match(files => files.DisposeAsync().AsTask(), () => Task.CompletedTask);

        foreach (var proxy in gateways.Proxies.Values)
        {
            await proxy.DisposeAsync();
        }

        await leases.ReleaseAsync(Holder(job), cancellationToken);

        return true;
    }

    private bool AllowNetworkOwned(JobId job, bool allowed)
    {
        if (!jobs.TryGetValue(job, out var gateways))
        {
            return false;
        }

        _ = gateways.Files.Match(files => files.AllowsNetwork = allowed, () => allowed);

        foreach (var proxy in gateways.Proxies.Values)
        {
            proxy.AllowsNetwork = allowed;
        }

        Keep(job, gateways with { Network = allowed });

        return true;
    }
}
