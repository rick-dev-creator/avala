using System.Collections.Immutable;
using Avala.Resources.Contracts;
using Avala.Resources.Leases;
using Avala.Resources.Tracking;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Sdk.Processes;
using Microsoft.Extensions.Logging;

namespace Avala.Resources.Leasing;

internal sealed partial class PortLeases(IListeningPorts listening, IResourceSettings settings, IEventBus bus, ILogger<PortLeases> logger)
    : IProcessEnvironment, IPortLeases, IAsyncDisposable
{
    private readonly SerialExecutor owner = new();
    private Option<PortBook> book;
    private ImmutableList<PortLease> current = [];

    public IReadOnlyList<PortLease> Current => Volatile.Read(ref current);

    public async ValueTask<IReadOnlyDictionary<string, string>> ForAsync(string home, CancellationToken cancellationToken) =>
        (await LeaseAsync(Folders.Key(home), own: false, cancellationToken)).Match(PortBook.Variables, () => ImmutableDictionary<string, string>.Empty);

    public ValueTask<Option<PortLease>> LeaseAsync(string holder, CancellationToken cancellationToken) => LeaseAsync(holder, own: true, cancellationToken);

    public Task ReleaseWorktreeAsync(string home, CancellationToken cancellationToken) => ReleaseAsync(Folders.Key(home), cancellationToken);

    public async Task ReleaseAsync(string holder, CancellationToken cancellationToken) =>
        await (await owner.RunAsync(_ => Task.FromResult(Release(holder)), cancellationToken)).Match(
            released => bus.PublishAsync(new PortsReleased(released), cancellationToken).AsTask(),
            () => Task.CompletedTask);

    public ValueTask DisposeAsync() => owner.DisposeAsync();

    private async ValueTask<Option<PortLease>> LeaseAsync(string holder, bool own, CancellationToken cancellationToken)
    {
        var range = (await settings.LoadAsync(cancellationToken)).Ports;
        var busy = (await listening.ListAsync(ImmutableHashSet<int>.Empty, cancellationToken)).Select(listener => listener.Port).ToHashSet();
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
        var (next, lease) = opened.Lease(worktree, busy, own);
        Keep(next);

        return (lease, opened.Of(worktree).IsNone && lease.IsSome);
    }

    private Option<PortLease> Release(string worktree) =>
        book.Match(
            known =>
            {
                var (next, released) = known.Release(worktree);
                Keep(next);

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
