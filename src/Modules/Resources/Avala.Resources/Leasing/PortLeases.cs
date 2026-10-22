using System.Collections.Immutable;
using Avala.Resources.Contracts;
using Avala.Resources.Leases;
using Avala.Resources.Tracking;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Sdk.Processes;
using Microsoft.Extensions.Logging;

namespace Avala.Resources.Leasing;

internal sealed partial class PortLeases(IPortClaims claims, IResourceSettings settings, IEventBus bus, ILogger<PortLeases> logger)
    : IProcessEnvironment, IPortLeases, IAsyncDisposable
{
    private readonly SerialExecutor owner = new();
    private Option<PortBook> book;
    private ImmutableList<PortLease> current = [];
    private ImmutableDictionary<string, IDisposable> claimed = ImmutableDictionary<string, IDisposable>.Empty;

    public IReadOnlyList<PortLease> Current => Volatile.Read(ref current);

    public async ValueTask<IReadOnlyDictionary<string, string>> ForAsync(string home, CancellationToken cancellationToken) =>
        (await LeaseAsync(Folders.Key(home), own: false, cancellationToken)).Match(PortBook.Variables, () => ImmutableDictionary<string, string>.Empty);

    public ValueTask<Option<PortLease>> LeaseAsync(string holder, CancellationToken cancellationToken) => LeaseAsync(holder, own: true, cancellationToken);

    public Task ReleaseWorktreeAsync(string home, CancellationToken cancellationToken) => ReleaseAsync(Folders.Key(home), cancellationToken);

    public async Task ReleaseAsync(string holder, CancellationToken cancellationToken) =>
        await (await owner.RunAsync(_ => Task.FromResult(Release(holder)), cancellationToken)).Match(
            released => bus.PublishAsync(new PortsReleased(released), cancellationToken).AsTask(),
            () => Task.CompletedTask);

    public async ValueTask DisposeAsync()
    {
        await owner.DisposeAsync();

        foreach (var claim in claimed.Values)
        {
            claim.Dispose();
        }
    }

    private async ValueTask<Option<PortLease>> LeaseAsync(string holder, bool own, CancellationToken cancellationToken)
    {
        var range = (await settings.LoadAsync(cancellationToken)).Ports;
        var busy = await claims.ListenedAsync(cancellationToken);
        var (lease, fresh) = await owner.RunAsync(_ => Task.FromResult(Lease(range, holder, busy, own)), cancellationToken);

        if (lease.IsNone)
        {
            LogExhausted(holder);
        }

        if (fresh)
        {
            await lease.Match(leased => bus.PublishAsync(new PortsLeased(leased), cancellationToken).AsTask(), () => Task.CompletedTask);
        }

        return lease;
    }

    private (Option<PortLease> Lease, bool Fresh) Lease(PortRange range, string worktree, IReadOnlySet<int> busy, bool own)
    {
        var opened = book.Match(known => known, () => PortBook.Open(range));

        if (opened.Of(worktree).IsSome)
        {
            return (opened.Of(worktree), false);
        }

        var (next, lease) = Claim(opened, worktree, busy, own);
        Keep(next);

        return (lease, lease.IsSome);
    }

    private (PortBook Book, Option<PortLease> Lease) Claim(PortBook opened, string worktree, IReadOnlySet<int> taken, bool own)
    {
        var (next, lease) = opened.Lease(worktree, taken, own);

        return lease.Match(
            block => Claimed(block) ? (next, lease) : Claim(opened, worktree, taken.Union(Enumerable.Range(block.First, block.Last - block.First + 1)).ToHashSet(), own),
            () => (opened, lease));
    }

    private bool Claimed(PortLease lease) =>
        claims.TryClaim(lease.First, lease.Last).Match(
            claim =>
            {
                claimed = claimed.SetItem(lease.Worktree, claim);

                return true;
            },
            () => false);

    private Option<PortLease> Release(string worktree) =>
        book.Match(
            known =>
            {
                var (next, released) = known.Release(worktree);
                Keep(next);

                if (claimed.TryGetValue(worktree, out var claim))
                {
                    claim.Dispose();
                    claimed = claimed.Remove(worktree);
                }

                return released;
            },
            () => Option<PortLease>.None);

    private void Keep(PortBook next)
    {
        book = next;
        Volatile.Write(ref current, next.Leases);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No port range is free for the worktree {Worktree}; its processes get no leased ports")]
    private partial void LogExhausted(string worktree);
}
